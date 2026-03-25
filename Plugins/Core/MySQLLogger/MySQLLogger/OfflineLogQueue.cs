using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using log4net;
using Npgsql;
using pGina.Shared.Types;

namespace pGina.Plugin.MySqlLogger
{
    class OfflineLogQueue
    {
        private class QueuedLogEntry
        {
            public long Id;
            public int Mode;
            public string Reason;
            public int SessionId;
            public string Username;
            public string Machine;
            public string IpAddress;
            public string Message;
            public DateTime EventUtc;
        }

        private static readonly ILog m_logger = LogManager.GetLogger("MySqlLoggerPlugin.OfflineQueue");
        private static readonly object m_syncRoot = new object();

        public static void Initialize()
        {
            lock (m_syncRoot)
            {
                SQLiteNativeBootstrap.EnsureInitialized();

                string dbPath = Settings.GetOfflineQueuePath();
                string directory = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                if (!File.Exists(dbPath))
                    SQLiteConnection.CreateFile(dbPath);

                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "CREATE TABLE IF NOT EXISTS queued_logs (" +
                        "id INTEGER PRIMARY KEY AUTOINCREMENT, " +
                        "mode INTEGER NOT NULL, " +
                        "reason TEXT NOT NULL, " +
                        "session_id INTEGER NOT NULL, " +
                        "username TEXT NULL, " +
                        "machine TEXT NOT NULL, " +
                        "ip_address TEXT NULL, " +
                        "message TEXT NULL, " +
                        "event_utc TEXT NOT NULL);";
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public static void Enqueue(LoggerMode mode, System.ServiceProcess.SessionChangeDescription changeDescription, SessionProperties properties)
        {
            if (!Settings.IsOfflineQueueEnabled())
                return;

            lock (m_syncRoot)
            {
                Initialize();

                string username = GetUsername(properties);
                string reason = changeDescription.Reason.ToString();
                string message = mode == LoggerMode.EVENT
                    ? BuildEventMessage(changeDescription.Reason, changeDescription.SessionId, username)
                    : null;

                if (mode == LoggerMode.EVENT && string.IsNullOrEmpty(message))
                    return;

                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "INSERT INTO queued_logs (mode, reason, session_id, username, machine, ip_address, message, event_utc) " +
                        "VALUES (@mode, @reason, @session_id, @username, @machine, @ip_address, @message, @event_utc)";
                    cmd.Parameters.AddWithValue("@mode", (int)mode);
                    cmd.Parameters.AddWithValue("@reason", reason);
                    cmd.Parameters.AddWithValue("@session_id", changeDescription.SessionId);
                    cmd.Parameters.AddWithValue("@username", (object)username ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@machine", Environment.MachineName);
                    cmd.Parameters.AddWithValue("@ip_address", (object)GetIpAddress() ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@message", (object)message ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@event_utc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public static void FlushPending()
        {
            if (!Settings.IsOfflineQueueEnabled())
                return;

            lock (m_syncRoot)
            {
                Initialize();

                List<QueuedLogEntry> queuedEntries = ReadPending(Settings.GetFlushBatchSize());
                if (queuedEntries.Count == 0)
                    return;

                using (var dbConn = LoggerModeFactory.CreateConnection())
                {
                    dbConn.Open();

                    foreach (QueuedLogEntry entry in queuedEntries)
                    {
                        ReplayEntry(dbConn, entry);
                        DeleteEntry(entry.Id);
                    }
                }

                m_logger.InfoFormat("Flushed {0} offline log entries to {1}.", queuedEntries.Count, Settings.GetDatabaseProvider());
            }
        }

        private static List<QueuedLogEntry> ReadPending(int batchSize)
        {
            var entries = new List<QueuedLogEntry>();

            using (var conn = OpenConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT id, mode, reason, session_id, username, machine, ip_address, message, event_utc " +
                    "FROM queued_logs ORDER BY id ASC LIMIT @limit";
                cmd.Parameters.AddWithValue("@limit", batchSize);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        entries.Add(new QueuedLogEntry
                        {
                            Id = Convert.ToInt64(reader["id"]),
                            Mode = Convert.ToInt32(reader["mode"]),
                            Reason = Convert.ToString(reader["reason"]),
                            SessionId = Convert.ToInt32(reader["session_id"]),
                            Username = reader["username"] == DBNull.Value ? null : Convert.ToString(reader["username"]),
                            Machine = Convert.ToString(reader["machine"]),
                            IpAddress = reader["ip_address"] == DBNull.Value ? null : Convert.ToString(reader["ip_address"]),
                            Message = reader["message"] == DBNull.Value ? null : Convert.ToString(reader["message"]),
                            EventUtc = DateTime.Parse(Convert.ToString(reader["event_utc"]), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                        });
                    }
                }
            }

            return entries;
        }

        private static void ReplayEntry(DbConnection dbConn, QueuedLogEntry entry)
        {
            if (entry.Mode == (int)LoggerMode.EVENT)
            {
                ReplayEventLog(dbConn, entry);
                return;
            }

            ReplaySessionLog(dbConn, entry);
        }

        private static void ReplayEventLog(DbConnection dbConn, QueuedLogEntry entry)
        {
            string sql = string.Format(
                "INSERT INTO {0}({1}, {2}, {3}, {4}, {5}) VALUES (@timeStamp, @host, @ip, @machine, @message)",
                Quote(Settings.Store.EventTable, dbConn),
                QuoteColumn("TimeStamp", dbConn),
                QuoteColumn("Host", dbConn),
                QuoteColumn("Ip", dbConn),
                QuoteColumn("Machine", dbConn),
                QuoteColumn("Message", dbConn));

            using (var cmd = dbConn.CreateCommand())
            {
                cmd.CommandText = sql;
                AddParameter(cmd, "@timeStamp", entry.EventUtc);
                AddParameter(cmd, "@host", Dns.GetHostName());
                AddParameter(cmd, "@ip", (object)entry.IpAddress ?? DBNull.Value);
                AddParameter(cmd, "@machine", entry.Machine);
                AddParameter(cmd, "@message", entry.Message ?? string.Empty);
                cmd.ExecuteNonQuery();
            }
        }

        private static void ReplaySessionLog(DbConnection dbConn, QueuedLogEntry entry)
        {
            string table = Settings.Store.SessionTable;
            string updateSql = string.Format(
                "UPDATE {0} SET {1}=@logoutstamp WHERE {1} IS NULL AND {2}=@username AND {3}=@machine AND {4}=@ipaddress",
                Quote(table, dbConn),
                QuoteColumn("logoutstamp", dbConn),
                QuoteColumn("username", dbConn),
                QuoteColumn("machine", dbConn),
                QuoteColumn("ipaddress", dbConn));

            if (string.Equals(entry.Reason, "SessionLogon", StringComparison.OrdinalIgnoreCase))
            {
                using (var updateCmd = dbConn.CreateCommand())
                {
                    updateCmd.CommandText = updateSql;
                    AddParameter(updateCmd, "@logoutstamp", entry.EventUtc);
                    AddParameter(updateCmd, "@username", entry.Username ?? "--UNKNOWN--");
                    AddParameter(updateCmd, "@machine", entry.Machine);
                    AddParameter(updateCmd, "@ipaddress", (object)entry.IpAddress ?? DBNull.Value);
                    updateCmd.ExecuteNonQuery();
                }

                string insertSql = dbConn is NpgsqlConnection
                    ? string.Format(
                        "INSERT INTO {0} ({1}, {2}, {3}, {4}, {5}) VALUES (@loginstamp, NULL, @username, @machine, @ipaddress)",
                        Quote(table, dbConn),
                        QuoteColumn("loginstamp", dbConn),
                        QuoteColumn("logoutstamp", dbConn),
                        QuoteColumn("username", dbConn),
                        QuoteColumn("machine", dbConn),
                        QuoteColumn("ipaddress", dbConn))
                    : string.Format(
                        "INSERT INTO {0} (dbid, loginstamp, logoutstamp, username, machine, ipaddress) VALUES (NULL, @loginstamp, NULL, @username, @machine, @ipaddress)",
                        Quote(table, dbConn));

                using (var insertCmd = dbConn.CreateCommand())
                {
                    insertCmd.CommandText = insertSql;
                    AddParameter(insertCmd, "@loginstamp", entry.EventUtc);
                    AddParameter(insertCmd, "@username", entry.Username ?? "--UNKNOWN--");
                    AddParameter(insertCmd, "@machine", entry.Machine);
                    AddParameter(insertCmd, "@ipaddress", (object)entry.IpAddress ?? DBNull.Value);
                    insertCmd.ExecuteNonQuery();
                }
            }
            else if (string.Equals(entry.Reason, "SessionLogoff", StringComparison.OrdinalIgnoreCase))
            {
                using (var updateCmd = dbConn.CreateCommand())
                {
                    updateCmd.CommandText = updateSql;
                    AddParameter(updateCmd, "@logoutstamp", entry.EventUtc);
                    AddParameter(updateCmd, "@username", entry.Username ?? "--UNKNOWN--");
                    AddParameter(updateCmd, "@machine", entry.Machine);
                    AddParameter(updateCmd, "@ipaddress", (object)entry.IpAddress ?? DBNull.Value);
                    updateCmd.ExecuteNonQuery();
                }
            }
        }

        private static void DeleteEntry(long id)
        {
            using (var conn = OpenConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM queued_logs WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
        }

        public static string TestConfiguration()
        {
            var sb = new StringBuilder();
            string dbPath = Settings.GetOfflineQueuePath();
            string nativeDirectory = SQLiteNativeBootstrap.GetNativeDirectory();
            string nativeDllPath = SQLiteNativeBootstrap.GetNativeDllPath();

            sb.AppendLine("Offline queue");
            sb.AppendLine("-------------------------------");
            sb.AppendLine(string.Format("Process architecture: {0}", Environment.Is64BitProcess ? "x64" : "x86"));
            sb.AppendLine(string.Format("Native SQLite dir: {0}", nativeDirectory));
            sb.AppendLine(string.Format("Native SQLite dll: {0}", File.Exists(nativeDllPath) ? nativeDllPath : "MISSING"));
            sb.AppendLine(string.Format("Queue file: {0}", dbPath));

            try
            {
                Initialize();
                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT 1";
                    cmd.ExecuteScalar();
                }

                sb.AppendLine("SQLite offline queue: OK");
            }
            catch (Exception ex)
            {
                sb.AppendLine(string.Format("SQLite offline queue ERROR: {0}", ex.Message));
            }

            return sb.ToString();
        }

        private static SQLiteConnection OpenConnection()
        {
            SQLiteNativeBootstrap.EnsureInitialized();
            var conn = new SQLiteConnection(string.Format("Data Source={0};Version=3;", Settings.GetOfflineQueuePath()));
            conn.Open();
            return conn;
        }

        private static string GetUsername(SessionProperties properties)
        {
            if (properties == null)
                return "--UNKNOWN--";

            UserInformation userInfo = properties.GetTrackedSingle<UserInformation>();
            if (userInfo == null)
                return "--UNKNOWN--";

            string username = Settings.GetUseModifiedName() ? userInfo.Username : userInfo.OriginalUsername;
            return string.IsNullOrWhiteSpace(username) ? "--UNKNOWN--" : username;
        }

        private static string GetIpAddress()
        {
            foreach (IPAddress addr in Dns.GetHostAddresses(string.Empty))
            {
                if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return addr.ToString();
            }

            return string.Empty;
        }

        private static string BuildEventMessage(System.ServiceProcess.SessionChangeReason reason, int sessionId, string username)
        {
            switch (reason)
            {
                case System.ServiceProcess.SessionChangeReason.SessionLogon:
                    return Settings.GetEvtLogon() ? string.Format("[{0}] Logon user: {1}", sessionId, username ?? "--Unknown--") : string.Empty;
                case System.ServiceProcess.SessionChangeReason.SessionLogoff:
                    return Settings.GetEvtLogoff() ? string.Format("[{0}] Logoff user: {1}", sessionId, username ?? "--Unknown--") : string.Empty;
                case System.ServiceProcess.SessionChangeReason.SessionLock:
                    return Settings.GetEvtLock() ? string.Format("[{0}] Session lock user: {1}", sessionId, username ?? "--Unknown--") : string.Empty;
                case System.ServiceProcess.SessionChangeReason.SessionUnlock:
                    return Settings.GetEvtUnlock() ? string.Format("[{0}] Session unlock user: {1}", sessionId, username ?? "--Unknown--") : string.Empty;
                case System.ServiceProcess.SessionChangeReason.SessionRemoteControl:
                    return Settings.GetEvtRemoteControl() ? string.Format("[{0}] Remote control user: {1}", sessionId, username ?? "--Unknown--") : string.Empty;
                case System.ServiceProcess.SessionChangeReason.ConsoleConnect:
                    return Settings.GetEvtConsoleConnect() ? string.Format("[{0}] Console connect", sessionId) : string.Empty;
                case System.ServiceProcess.SessionChangeReason.ConsoleDisconnect:
                    return Settings.GetEvtConsoleDisconnect() ? string.Format("[{0}] Console disconnect", sessionId) : string.Empty;
                case System.ServiceProcess.SessionChangeReason.RemoteConnect:
                    return Settings.GetEvtRemoteConnect() ? string.Format("[{0}] Remote connect user: {1}", sessionId, username ?? "--Unknown--") : string.Empty;
                case System.ServiceProcess.SessionChangeReason.RemoteDisconnect:
                    return Settings.GetEvtRemoteDisconnect() ? string.Format("[{0}] Remote disconnect user: {1}", sessionId, username ?? "--Unknown--") : string.Empty;
                default:
                    return string.Empty;
            }
        }

        private static void AddParameter(DbCommand cmd, string name, object value)
        {
            var parameter = cmd.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(parameter);
        }

        private static string Quote(string identifier, DbConnection dbConn)
        {
            return dbConn is NpgsqlConnection
                ? "\"" + identifier.Replace("\"", "\"\"") + "\""
                : "`" + identifier.Replace("`", "``") + "`";
        }

        private static string QuoteColumn(string identifier, DbConnection dbConn)
        {
            return dbConn is NpgsqlConnection ? Quote(identifier, dbConn) : identifier;
        }
    }
}
