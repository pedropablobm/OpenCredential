using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Net;
using log4net;
using MySqlConnector;
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

                using (var mysqlConn = LoggerModeFactory.CreateConnection())
                {
                    mysqlConn.Open();

                    foreach (QueuedLogEntry entry in queuedEntries)
                    {
                        ReplayEntry(mysqlConn, entry);
                        DeleteEntry(entry.Id);
                    }
                }

                m_logger.InfoFormat("Flushed {0} offline log entries to MySQL.", queuedEntries.Count);
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

        private static void ReplayEntry(MySqlConnection mysqlConn, QueuedLogEntry entry)
        {
            if (entry.Mode == (int)LoggerMode.EVENT)
            {
                ReplayEventLog(mysqlConn, entry);
                return;
            }

            ReplaySessionLog(mysqlConn, entry);
        }

        private static void ReplayEventLog(MySqlConnection mysqlConn, QueuedLogEntry entry)
        {
            string sql = string.Format(
                "INSERT INTO `{0}`(TimeStamp, Host, Ip, Machine, Message) VALUES (@timeStamp, @host, @ip, @machine, @message)",
                Settings.Store.EventTable);

            using (var cmd = new MySqlCommand(sql, mysqlConn))
            {
                cmd.Parameters.AddWithValue("@timeStamp", entry.EventUtc);
                cmd.Parameters.AddWithValue("@host", Dns.GetHostName());
                cmd.Parameters.AddWithValue("@ip", (object)entry.IpAddress ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@machine", entry.Machine);
                cmd.Parameters.Add("@message", MySqlDbType.Text).Value = entry.Message ?? string.Empty;
                cmd.ExecuteNonQuery();
            }
        }

        private static void ReplaySessionLog(MySqlConnection mysqlConn, QueuedLogEntry entry)
        {
            string table = Settings.Store.SessionTable;

            if (string.Equals(entry.Reason, "SessionLogon", StringComparison.OrdinalIgnoreCase))
            {
                string updateSql = string.Format(
                    "UPDATE `{0}` SET logoutstamp=@logoutstamp WHERE logoutstamp IS NULL AND machine=@machine AND ipaddress=@ipaddress",
                    table);
                using (var updateCmd = new MySqlCommand(updateSql, mysqlConn))
                {
                    updateCmd.Parameters.AddWithValue("@logoutstamp", entry.EventUtc);
                    updateCmd.Parameters.AddWithValue("@machine", entry.Machine);
                    updateCmd.Parameters.AddWithValue("@ipaddress", (object)entry.IpAddress ?? DBNull.Value);
                    updateCmd.ExecuteNonQuery();
                }

                string insertSql = string.Format(
                    "INSERT INTO `{0}` (dbid, loginstamp, logoutstamp, username, machine, ipaddress) " +
                    "VALUES (NULL, @loginstamp, NULL, @username, @machine, @ipaddress)",
                    table);
                using (var insertCmd = new MySqlCommand(insertSql, mysqlConn))
                {
                    insertCmd.Parameters.AddWithValue("@loginstamp", entry.EventUtc);
                    insertCmd.Parameters.AddWithValue("@username", entry.Username ?? "--UNKNOWN--");
                    insertCmd.Parameters.AddWithValue("@machine", entry.Machine);
                    insertCmd.Parameters.AddWithValue("@ipaddress", (object)entry.IpAddress ?? DBNull.Value);
                    insertCmd.ExecuteNonQuery();
                }
            }
            else if (string.Equals(entry.Reason, "SessionLogoff", StringComparison.OrdinalIgnoreCase))
            {
                string updateSql = string.Format(
                    "UPDATE `{0}` SET logoutstamp=@logoutstamp WHERE logoutstamp IS NULL AND username=@username AND machine=@machine AND ipaddress=@ipaddress",
                    table);
                using (var updateCmd = new MySqlCommand(updateSql, mysqlConn))
                {
                    updateCmd.Parameters.AddWithValue("@logoutstamp", entry.EventUtc);
                    updateCmd.Parameters.AddWithValue("@username", entry.Username ?? "--UNKNOWN--");
                    updateCmd.Parameters.AddWithValue("@machine", entry.Machine);
                    updateCmd.Parameters.AddWithValue("@ipaddress", (object)entry.IpAddress ?? DBNull.Value);
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

            return Settings.GetUseModifiedName() ? userInfo.Username : userInfo.OriginalUsername;
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
    }
}
