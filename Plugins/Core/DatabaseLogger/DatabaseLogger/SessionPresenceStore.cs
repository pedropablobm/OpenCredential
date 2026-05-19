using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;

namespace OpenCredential.Plugin.DatabaseLogger
{
    internal sealed class SessionPresenceState
    {
        public int WindowsSessionId { get; set; }
        public string Username { get; set; }
        public string ClientSessionId { get; set; }
        public string Machine { get; set; }
        public string IpAddress { get; set; }
        public string SessionState { get; set; }
        public DateTime LastHeartbeatUtc { get; set; }
    }

    internal static class SessionPresenceStore
    {
        private static readonly object SyncRoot = new object();

        public static void Initialize()
        {
            lock (SyncRoot)
            {
                SQLiteNativeBootstrap.EnsureInitialized();

                string dbPath = Settings.GetPresenceStatePath();
                string directory = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                if (!File.Exists(dbPath))
                    SQLiteConnection.CreateFile(dbPath);

                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "CREATE TABLE IF NOT EXISTS active_sessions (" +
                        "windows_session_id INTEGER PRIMARY KEY, " +
                        "username TEXT NULL, " +
                        "client_session_id TEXT NULL, " +
                        "machine TEXT NULL, " +
                        "ip_address TEXT NULL, " +
                        "session_state TEXT NOT NULL, " +
                        "last_heartbeat_utc TEXT NOT NULL);";
                    cmd.ExecuteNonQuery();
                }

                EnsureColumn("active_sessions", "client_session_id", "TEXT NULL");
                EnsureColumn("active_sessions", "machine", "TEXT NULL");
                EnsureColumn("active_sessions", "ip_address", "TEXT NULL");
            }
        }

        public static void Upsert(SessionPresenceState state)
        {
            if (state == null)
                return;

            lock (SyncRoot)
            {
                Initialize();

                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "INSERT INTO active_sessions (windows_session_id, username, client_session_id, machine, ip_address, session_state, last_heartbeat_utc) " +
                        "VALUES (@windows_session_id, @username, @client_session_id, @machine, @ip_address, @session_state, @last_heartbeat_utc) " +
                        "ON CONFLICT(windows_session_id) DO UPDATE SET " +
                        "username = excluded.username, " +
                        "client_session_id = excluded.client_session_id, " +
                        "machine = excluded.machine, " +
                        "ip_address = excluded.ip_address, " +
                        "session_state = excluded.session_state, " +
                        "last_heartbeat_utc = excluded.last_heartbeat_utc;";
                    cmd.Parameters.AddWithValue("@windows_session_id", state.WindowsSessionId);
                    cmd.Parameters.AddWithValue("@username", (object)state.Username ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@client_session_id", (object)state.ClientSessionId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@machine", (object)state.Machine ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ip_address", (object)state.IpAddress ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@session_state", string.IsNullOrWhiteSpace(state.SessionState) ? "active" : state.SessionState);
                    cmd.Parameters.AddWithValue("@last_heartbeat_utc", state.LastHeartbeatUtc.ToString("o", CultureInfo.InvariantCulture));
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public static List<SessionPresenceState> LoadAll()
        {
            lock (SyncRoot)
            {
                Initialize();

                var states = new List<SessionPresenceState>();
                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT windows_session_id, username, client_session_id, machine, ip_address, session_state, last_heartbeat_utc " +
                        "FROM active_sessions";

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            states.Add(new SessionPresenceState
                            {
                                WindowsSessionId = Convert.ToInt32(reader["windows_session_id"]),
                                Username = reader["username"] == DBNull.Value ? null : Convert.ToString(reader["username"]),
                                ClientSessionId = reader["client_session_id"] == DBNull.Value ? null : Convert.ToString(reader["client_session_id"]),
                                Machine = reader["machine"] == DBNull.Value ? null : Convert.ToString(reader["machine"]),
                                IpAddress = reader["ip_address"] == DBNull.Value ? null : Convert.ToString(reader["ip_address"]),
                                SessionState = Convert.ToString(reader["session_state"]),
                                LastHeartbeatUtc = DateTime.Parse(
                                    Convert.ToString(reader["last_heartbeat_utc"]),
                                    CultureInfo.InvariantCulture,
                                    DateTimeStyles.RoundtripKind)
                            });
                        }
                    }
                }

                return states;
            }
        }

        public static int GetActiveSessionCount()
        {
            lock (SyncRoot)
            {
                Initialize();

                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM active_sessions";
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
        }

        public static string GetUsername(int windowsSessionId)
        {
            lock (SyncRoot)
            {
                Initialize();

                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT username FROM active_sessions WHERE windows_session_id = @windows_session_id LIMIT 1";
                    cmd.Parameters.AddWithValue("@windows_session_id", windowsSessionId);
                    object value = cmd.ExecuteScalar();
                    return value == null || value == DBNull.Value ? null : Convert.ToString(value);
                }
            }
        }

        public static void Remove(int windowsSessionId)
        {
            lock (SyncRoot)
            {
                Initialize();

                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM active_sessions WHERE windows_session_id = @windows_session_id";
                    cmd.Parameters.AddWithValue("@windows_session_id", windowsSessionId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private static SQLiteConnection OpenConnection()
        {
            SQLiteNativeBootstrap.EnsureInitialized();
            var conn = new SQLiteConnection(string.Format("Data Source={0};Version=3;", Settings.GetPresenceStatePath()));
            conn.Open();
            return conn;
        }

        private static void EnsureColumn(string tableName, string columnName, string definition)
        {
            using (var conn = OpenConnection())
            {
                if (HasColumn(conn, tableName, columnName))
                    return;

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = string.Format(
                        "ALTER TABLE {0} ADD COLUMN {1} {2}",
                        tableName,
                        columnName,
                        definition);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private static bool HasColumn(SQLiteConnection conn, string tableName, string columnName)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(" + tableName + ")";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (string.Equals(Convert.ToString(reader["name"]), columnName, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }

            return false;
        }
    }
}
