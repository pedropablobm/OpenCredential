using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using log4net;

namespace pGina.Plugin.MySQLAuth
{
    class LocalUserCache
    {
        private static readonly ILog m_logger = LogManager.GetLogger("MySQLAuth.LocalUserCache");
        private static readonly object m_syncRoot = new object();

        public static void Initialize()
        {
            lock (m_syncRoot)
            {
                string dbPath = Settings.GetLocalCachePath();
                string directory = Path.GetDirectoryName(dbPath);

                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (!File.Exists(dbPath))
                {
                    SQLiteConnection.CreateFile(dbPath);
                }

                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "CREATE TABLE IF NOT EXISTS users (" +
                        "username TEXT PRIMARY KEY, " +
                        "hash_method TEXT NOT NULL, " +
                        "hash_value TEXT NOT NULL, " +
                        "status_value TEXT NULL, " +
                        "updated_utc TEXT NOT NULL);" +
                        "CREATE TABLE IF NOT EXISTS metadata (" +
                        "key TEXT PRIMARY KEY, " +
                        "value TEXT NULL);";
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public static void UpsertUser(UserEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
                return;

            UpsertUsers(new[] { entry });
        }

        public static void UpsertUsers(IEnumerable<UserEntry> entries)
        {
            if (entries == null)
                return;

            lock (m_syncRoot)
            {
                Initialize();

                using (var conn = OpenConnection())
                using (var transaction = conn.BeginTransaction())
                {
                    foreach (UserEntry entry in entries)
                    {
                        if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
                            continue;

                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = transaction;
                            cmd.CommandText =
                                "INSERT INTO users (username, hash_method, hash_value, status_value, updated_utc) " +
                                "VALUES (@username, @hash_method, @hash_value, @status_value, @updated_utc) " +
                                "ON CONFLICT(username) DO UPDATE SET " +
                                "hash_method = excluded.hash_method, " +
                                "hash_value = excluded.hash_value, " +
                                "status_value = excluded.status_value, " +
                                "updated_utc = excluded.updated_utc;";
                            cmd.Parameters.AddWithValue("@username", entry.Name);
                            cmd.Parameters.AddWithValue("@hash_method", entry.HashAlg.ToString());
                            cmd.Parameters.AddWithValue("@hash_value", entry.Hash ?? string.Empty);
                            cmd.Parameters.AddWithValue("@status_value", (object)entry.StatusValue ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@updated_utc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                            cmd.ExecuteNonQuery();
                        }
                    }

                    transaction.Commit();
                }
            }
        }

        public static bool TryAuthenticate(string username, string password, out UserEntry cachedEntry)
        {
            cachedEntry = null;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
                return false;

            lock (m_syncRoot)
            {
                Initialize();

                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT hash_method, hash_value, status_value FROM users WHERE username = @username";
                    cmd.Parameters.AddWithValue("@username", username);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                            return false;

                        string hashMethod = Convert.ToString(reader["hash_method"]);
                        string hashValue = Convert.ToString(reader["hash_value"]);
                        string statusValue = reader["status_value"] == DBNull.Value
                            ? null
                            : Convert.ToString(reader["status_value"]);
                        PasswordHashAlgorithm hashAlg;

                        if (!TryParseHashAlgorithm(hashMethod, out hashAlg))
                        {
                            m_logger.WarnFormat("Cached user {0} has unknown hash algorithm '{1}'", username, hashMethod);
                            return false;
                        }

                        cachedEntry = new UserEntry(username, hashAlg, hashValue, statusValue);

                        if (Settings.IsUserStatusValidationEnabled() &&
                            !string.Equals(cachedEntry.StatusValue, Settings.GetUserActiveValue(), StringComparison.OrdinalIgnoreCase))
                        {
                            m_logger.WarnFormat(
                                "Offline cache rejected user {0} because cached status '{1}' does not match '{2}'",
                                username,
                                cachedEntry.StatusValue,
                                Settings.GetUserActiveValue());
                            return false;
                        }

                        return cachedEntry.VerifyPassword(password);
                    }
                }
            }
        }

        public static DateTime? GetLastSyncUtc()
        {
            lock (m_syncRoot)
            {
                Initialize();

                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT value FROM metadata WHERE key = 'last_sync_utc'";
                    object value = cmd.ExecuteScalar();
                    DateTime parsed;

                    if (value != null &&
                        value != DBNull.Value &&
                        DateTime.TryParse(
                            Convert.ToString(value),
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind,
                            out parsed))
                    {
                        return parsed;
                    }
                }
            }

            return null;
        }

        public static void SetLastSyncUtc(DateTime value)
        {
            lock (m_syncRoot)
            {
                Initialize();

                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "INSERT INTO metadata (key, value) VALUES ('last_sync_utc', @value) " +
                        "ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
                    cmd.Parameters.AddWithValue("@value", value.ToString("o", CultureInfo.InvariantCulture));
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private static SQLiteConnection OpenConnection()
        {
            var conn = new SQLiteConnection(string.Format("Data Source={0};Version=3;", Settings.GetLocalCachePath()));
            conn.Open();
            return conn;
        }

        private static bool TryParseHashAlgorithm(string hashMethod, out PasswordHashAlgorithm hashAlg)
        {
            hashAlg = PasswordHashAlgorithm.NONE;

            if (string.IsNullOrWhiteSpace(hashMethod))
                return true;

            return Enum.TryParse(hashMethod.Trim(), true, out hashAlg);
        }
    }
}
