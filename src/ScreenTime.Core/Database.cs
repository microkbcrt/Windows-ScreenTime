using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace ScreenTime.Core;

public sealed record UsageEntry(string Date, int Minute, string App, string Site, double Seconds);

public sealed class Database : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly object _gate = new();

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScreenTime", "screentime.db");

    public Database(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
        _conn = new SqliteConnection(cs);
        _conn.Open();
        Exec("PRAGMA journal_mode=WAL");
        Exec("PRAGMA synchronous=NORMAL");
        EnsureSchema();
    }

    private void Exec(string sql)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }

    private void EnsureSchema()
    {
        Exec("""
            CREATE TABLE IF NOT EXISTS usage(
                date    TEXT NOT NULL,
                minute  INTEGER NOT NULL,
                app     TEXT NOT NULL,
                site    TEXT NOT NULL DEFAULT '',
                seconds REAL NOT NULL,
                PRIMARY KEY(date, minute, app, site)
            ) WITHOUT ROWID;
            CREATE INDEX IF NOT EXISTS idx_usage_date_app ON usage(date, app);
            CREATE TABLE IF NOT EXISTS events(
                ts   INTEGER NOT NULL,
                type TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_events_ts ON events(ts);
            CREATE TABLE IF NOT EXISTS app_names(app TEXT PRIMARY KEY, name TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS app_categories(app TEXT PRIMARY KEY, category TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS limits(app TEXT PRIMARY KEY, minutes INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT NOT NULL);
            """);
    }

    public void FlushUsage(IReadOnlyList<UsageEntry> entries, IReadOnlyDictionary<string, string> names)
    {
        if (entries.Count == 0 && names.Count == 0) return;
        lock (_gate)
        {
            using var tx = _conn.BeginTransaction();
            if (entries.Count > 0)
            {
                using var cmd = _conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO usage(date, minute, app, site, seconds)
                    VALUES($d, $m, $a, $s, $sec)
                    ON CONFLICT(date, minute, app, site)
                    DO UPDATE SET seconds = seconds + $sec
                    """;
                var pd = cmd.Parameters.Add("$d", SqliteType.Text);
                var pm = cmd.Parameters.Add("$m", SqliteType.Integer);
                var pa = cmd.Parameters.Add("$a", SqliteType.Text);
                var ps = cmd.Parameters.Add("$s", SqliteType.Text);
                var pv = cmd.Parameters.Add("$sec", SqliteType.Real);
                cmd.Prepare();
                foreach (var e in entries)
                {
                    pd.Value = e.Date;
                    pm.Value = e.Minute;
                    pa.Value = e.App;
                    ps.Value = e.Site;
                    pv.Value = e.Seconds;
                    cmd.ExecuteNonQuery();
                }
            }
            if (names.Count > 0)
            {
                using var cmd2 = _conn.CreateCommand();
                cmd2.Transaction = tx;
                cmd2.CommandText = """
                    INSERT INTO app_names(app, name) VALUES($a, $n)
                    ON CONFLICT(app) DO UPDATE SET name = $n
                    """;
                var pa = cmd2.Parameters.Add("$a", SqliteType.Text);
                var pn = cmd2.Parameters.Add("$n", SqliteType.Text);
                cmd2.Prepare();
                foreach (var (app, name) in names)
                {
                    pa.Value = app;
                    pn.Value = name;
                    cmd2.ExecuteNonQuery();
                }
            }
            tx.Commit();
        }
    }

    public void AddEvent(long ts, string type)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT INTO events(ts, type) VALUES($t, $y)";
            cmd.Parameters.AddWithValue("$t", ts);
            cmd.Parameters.AddWithValue("$y", type);
            cmd.ExecuteNonQuery();
        }
    }

    public Dictionary<string, double> GetAppTotals(string date)
    {
        lock (_gate)
        {
            var result = new Dictionary<string, double>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT app, SUM(seconds) FROM usage WHERE date=$d AND site='' GROUP BY app";
            cmd.Parameters.AddWithValue("$d", date);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result[reader.GetString(0)] = reader.GetDouble(1);
            return result;
        }
    }

    public Dictionary<string, double> GetAppTotalsRange(string startKey, string endKey)
    {
        lock (_gate)
        {
            var result = new Dictionary<string, double>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT app, SUM(seconds) FROM usage WHERE date BETWEEN $a AND $b AND site='' GROUP BY app";
            cmd.Parameters.AddWithValue("$a", startKey);
            cmd.Parameters.AddWithValue("$b", endKey);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result[reader.GetString(0)] = reader.GetDouble(1);
            return result;
        }
    }

    public double GetTotalSeconds(string date)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(SUM(seconds), 0) FROM usage WHERE date=$d";
            cmd.Parameters.AddWithValue("$d", date);
            return Convert.ToDouble(cmd.ExecuteScalar());
        }
    }

    /// <summary>24 小时分布（含网站行，网站行已归属到浏览器应用之外？否——网站行与 app 行同分钟并存，需排除站点行避免重复）。</summary>
    public double[] GetHourly(string date)
    {
        lock (_gate)
        {
            var hours = new double[24];
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT minute/60 AS h, SUM(seconds) FROM usage WHERE date=$d AND site='' GROUP BY h";
            cmd.Parameters.AddWithValue("$d", date);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var h = Convert.ToInt32(reader.GetValue(0));
                if (h is >= 0 and < 24) hours[h] = reader.GetDouble(1);
            }
            return hours;
        }
    }

    public Dictionary<string, double> GetDailyTotals(string startKey, string endKey)
    {
        lock (_gate)
        {
            var result = new Dictionary<string, double>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT date, SUM(seconds) FROM usage WHERE date BETWEEN $a AND $b AND site='' GROUP BY date";
            cmd.Parameters.AddWithValue("$a", startKey);
            cmd.Parameters.AddWithValue("$b", endKey);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result[reader.GetString(0)] = reader.GetDouble(1);
            return result;
        }
    }

    public Dictionary<string, double> GetAppDaily(string app, string startKey, string endKey)
    {
        lock (_gate)
        {
            var result = new Dictionary<string, double>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT date, SUM(seconds) FROM usage WHERE app=$a AND date BETWEEN $s AND $e AND site='' GROUP BY date";
            cmd.Parameters.AddWithValue("$a", app);
            cmd.Parameters.AddWithValue("$s", startKey);
            cmd.Parameters.AddWithValue("$e", endKey);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result[reader.GetString(0)] = reader.GetDouble(1);
            return result;
        }
    }

    public List<(string Site, double Seconds)> GetSiteTotals(string date, string app)
    {
        lock (_gate)
        {
            var list = new List<(string, double)>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT site, SUM(seconds) FROM usage WHERE date=$d AND app=$a AND site<>'' GROUP BY site ORDER BY 2 DESC LIMIT 20";
            cmd.Parameters.AddWithValue("$d", date);
            cmd.Parameters.AddWithValue("$a", app);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add((reader.GetString(0), reader.GetDouble(1)));
            return list;
        }
    }

    public long GetPickups(string date)
    {
        var start = UsageTime.ToUnix(DateTime.Parse(date));
        var end = UsageTime.ToUnix(DateTime.Parse(date).AddDays(1));
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM events WHERE type='pickup' AND ts>=$a AND ts<$b";
            cmd.Parameters.AddWithValue("$a", start);
            cmd.Parameters.AddWithValue("$b", end);
            return (long)(cmd.ExecuteScalar() ?? 0L);
        }
    }

    public List<(long Ts, string Type)> GetEvents(string date)
    {
        var start = UsageTime.ToUnix(DateTime.Parse(date));
        var end = UsageTime.ToUnix(DateTime.Parse(date).AddDays(1));
        lock (_gate)
        {
            var list = new List<(long, string)>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT ts, type FROM events WHERE ts>=$a AND ts<$b ORDER BY ts";
            cmd.Parameters.AddWithValue("$a", start);
            cmd.Parameters.AddWithValue("$b", end);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add((reader.GetInt64(0), reader.GetString(1)));
            return list;
        }
    }

    public Dictionary<string, string> GetAppNames()
    {
        lock (_gate)
        {
            var result = new Dictionary<string, string>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT app, name FROM app_names";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result[reader.GetString(0)] = reader.GetString(1);
            return result;
        }
    }

    public Dictionary<string, string> GetAppCategories()
    {
        lock (_gate)
        {
            var result = new Dictionary<string, string>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT app, category FROM app_categories";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result[reader.GetString(0)] = reader.GetString(1);
            return result;
        }
    }

    public void SetAppCategory(string app, string? category)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = category is null
                ? "DELETE FROM app_categories WHERE app=$a"
                : """
                  INSERT INTO app_categories(app, category) VALUES($a, $c)
                  ON CONFLICT(app) DO UPDATE SET category = $c
                  """;
            cmd.Parameters.AddWithValue("$a", app);
            if (category is not null) cmd.Parameters.AddWithValue("$c", category);
            cmd.ExecuteNonQuery();
        }
    }

    public Dictionary<string, int> GetLimits()
    {
        lock (_gate)
        {
            var result = new Dictionary<string, int>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT app, minutes FROM limits";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result[reader.GetString(0)] = reader.GetInt32(1);
            return result;
        }
    }

    public void SetLimit(string app, int? minutes)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = minutes is null
                ? "DELETE FROM limits WHERE app=$a"
                : """
                  INSERT INTO limits(app, minutes) VALUES($a, $m)
                  ON CONFLICT(app) DO UPDATE SET minutes = $m
                  """;
            cmd.Parameters.AddWithValue("$a", app);
            if (minutes is not null) cmd.Parameters.AddWithValue("$m", minutes.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public string? GetSetting(string key)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM settings WHERE key=$k";
            cmd.Parameters.AddWithValue("$k", key);
            return cmd.ExecuteScalar() as string;
        }
    }

    public void SetSetting(string key, string value)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO settings(key, value) VALUES($k, $v)
                ON CONFLICT(key) DO UPDATE SET value = $v
                """;
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", value);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>将数据库完整快照导出到指定文件（VACUUM INTO，含全部表与数据）。</summary>
    public void VacuumInto(string targetPath)
    {
        lock (_gate)
        {
            if (File.Exists(targetPath)) File.Delete(targetPath);
            var literal = targetPath.Replace("'", "''");
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = $"VACUUM INTO '{literal}'";
            cmd.ExecuteNonQuery();
        }
    }

    public void Dispose() => _conn.Dispose();
}
