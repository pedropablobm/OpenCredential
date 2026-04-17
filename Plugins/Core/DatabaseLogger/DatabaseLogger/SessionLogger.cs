// OpenCredential - Source code
// https://github.com/pedropablobm/OpenCredential
//
// Copyright (c) 2024, Pedro Bermudez
// All rights reserved.
//
// This file is part of OpenCredential, an unofficial fork of pGina.
// It is distributed under the BSD-3-Clause license terms used by the
// original pGina project unless stated otherwise.
//
// For more details, see the LICENSE and NOTICE files included with
// this distribution.

using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Net;
using System.ServiceProcess;
using log4net;
using Npgsql;
using OpenCredential.Shared.Types;

namespace OpenCredential.Plugin.DatabaseLogger
{
    class SessionLogger : ILoggerMode
    {
        private readonly ILog m_logger = LogManager.GetLogger("DatabaseLoggerPlugin");
        private DbConnection m_conn;

        public bool Log(SessionChangeDescription changeDescription, SessionProperties properties)
        {
            if (m_conn == null)
                throw new InvalidOperationException("No database connection present.");

            string username = "--UNKNOWN--";
            if (properties != null)
            {
                UserInformation ui = properties.GetTrackedSingle<UserInformation>();
                if (ui != null)
                    username = ResolveUsername(ui);
            }

            if (changeDescription.Reason == SessionChangeReason.SessionLogon)
            {
                EnsureConnection();
                UpdateOpenSessions(username, GetIpAddress(), NowExpression);

                string insertSql = IsPostgreSql
                    ? string.Format(
                        "INSERT INTO {0} ({1}, {2}, {3}, {4}, {5}) VALUES ({6}, NULL, @username, @machine, @ipaddress)",
                        Quote(Settings.Store.SessionTable),
                        QuoteColumn("loginstamp"),
                        QuoteColumn("logoutstamp"),
                        QuoteColumn("username"),
                        QuoteColumn("machine"),
                        QuoteColumn("ipaddress"),
                        NowExpression)
                    : string.Format(
                        "INSERT INTO {0} (dbid, loginstamp, logoutstamp, username, machine, ipaddress) VALUES (NULL, NOW(), NULL, @username, @machine, @ipaddress)",
                        Quote(Settings.Store.SessionTable));

                using (var cmd = m_conn.CreateCommand())
                {
                    cmd.CommandText = insertSql;
                    AddParameter(cmd, "@username", username);
                    AddParameter(cmd, "@machine", Environment.MachineName);
                    AddParameter(cmd, "@ipaddress", GetIpAddress());
                    cmd.ExecuteNonQuery();
                }

                m_logger.DebugFormat("Logged LogOn event for {0}", username);
            }
            else if (changeDescription.Reason == SessionChangeReason.SessionLogoff)
            {
                EnsureConnection();
                UpdateOpenSessions(username, GetIpAddress(), "@logoutstamp", DateTime.UtcNow);
                m_logger.DebugFormat("Logged LogOff event for {0}", username);
            }

            return true;
        }

        public string TestTable()
        {
            EnsureConnection();

            try
            {
                string table = Convert.ToString(Settings.Store.SessionTable);
                if (!TableExists(table))
                    return "Connection successful, but table does not exist. Click 'Create Table'.";

                string[] columns = { "dbid", "loginstamp", "logoutstamp", "username", "machine", "ipaddress" };
                using (var cmd = m_conn.CreateCommand())
                {
                    cmd.CommandText = IsPostgreSql
                        ? "SELECT column_name FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = @table ORDER BY ordinal_position"
                        : "DESCRIBE " + Quote(table);
                    if (IsPostgreSql)
                        AddParameter(cmd, "@table", table);

                    using (var rdr = cmd.ExecuteReader())
                    {
                        int colCt = 0;
                        while (rdr.Read())
                        {
                            if (!columns.Contains(Convert.ToString(rdr[0])))
                                return "Table exists but has invalid columns.";
                            colCt++;
                        }

                        return colCt == columns.Length ? "Table exists and is correct." : "Table has incorrect columns.";
                    }
                }
            }
            catch (Exception ex)
            {
                return string.Format("Error: {0}", ex.Message);
            }
        }

        public string CreateTable()
        {
            EnsureConnection();

            try
            {
                string table = Convert.ToString(Settings.Store.SessionTable);
                if (TableExists(table))
                    return "Table already exists.";

                string sql = IsPostgreSql
                    ? string.Format(
                        "CREATE TABLE {0} (\"dbid\" BIGSERIAL PRIMARY KEY, \"loginstamp\" TIMESTAMP NOT NULL, \"logoutstamp\" TIMESTAMP NULL, \"username\" VARCHAR(128) NOT NULL, \"machine\" VARCHAR(128) NOT NULL, \"ipaddress\" VARCHAR(45) NOT NULL)",
                        Quote(table))
                    : string.Format(
                        "CREATE TABLE {0} (dbid BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, loginstamp DATETIME NOT NULL, logoutstamp DATETIME NULL, username VARCHAR(128) NOT NULL, machine VARCHAR(128) NOT NULL, ipaddress VARCHAR(45) NOT NULL, INDEX idx_{1}_active (logoutstamp, machine, ipaddress), INDEX idx_{1}_user (username, machine, ipaddress)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",
                        Quote(table),
                        table);

                using (var cmd = m_conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                }

                if (IsPostgreSql)
                {
                    using (var cmd = m_conn.CreateCommand())
                    {
                        cmd.CommandText = string.Format(
                            "CREATE INDEX {0} ON {1} (\"logoutstamp\", \"machine\", \"ipaddress\"); " +
                            "CREATE INDEX {2} ON {1} (\"username\", \"machine\", \"ipaddress\")",
                            Quote("idx_" + table + "_active"),
                            Quote(table),
                            Quote("idx_" + table + "_user"));
                        cmd.ExecuteNonQuery();
                    }
                }

                return "Table created.";
            }
            catch (Exception ex)
            {
                return string.Format("Error: {0}", ex.Message);
            }
        }

        public void SetConnection(DbConnection connection)
        {
            m_conn = connection;
        }

        private void UpdateOpenSessions(string username, string ipAddress, string logoutExpression, object logoutParameterValue = null)
        {
            string updateSql = string.Format(
                "UPDATE {0} SET {1}={2} WHERE {1} IS NULL AND {3}=@username AND {4}=@machine AND {5}=@ipaddress",
                Quote(Settings.Store.SessionTable),
                QuoteColumn("logoutstamp"),
                logoutExpression,
                QuoteColumn("username"),
                QuoteColumn("machine"),
                QuoteColumn("ipaddress"));

            using (var cmd = m_conn.CreateCommand())
            {
                cmd.CommandText = updateSql;
                AddParameter(cmd, "@username", username);
                AddParameter(cmd, "@machine", Environment.MachineName);
                AddParameter(cmd, "@ipaddress", ipAddress);
                if (logoutParameterValue != null)
                    AddParameter(cmd, "@logoutstamp", logoutParameterValue);
                cmd.ExecuteNonQuery();
            }
        }

        private bool TableExists(string table)
        {
            using (var cmd = m_conn.CreateCommand())
            {
                if (IsPostgreSql)
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = @table";
                    AddParameter(cmd, "@table", table);
                    return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                }

                cmd.CommandText = "SHOW TABLES LIKE @table";
                AddParameter(cmd, "@table", table);
                using (var rdr = cmd.ExecuteReader())
                    return rdr.Read();
            }
        }

        private void EnsureConnection()
        {
            if (m_conn == null)
                throw new InvalidOperationException("No database connection present.");

            if (m_conn.State != ConnectionState.Open)
                m_conn.Open();
        }

        private void AddParameter(DbCommand cmd, string name, object value)
        {
            var parameter = cmd.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(parameter);
        }

        private string Quote(string identifier)
        {
            return IsPostgreSql
                ? "\"" + identifier.Replace("\"", "\"\"") + "\""
                : "`" + identifier.Replace("`", "``") + "`";
        }

        private string QuoteColumn(string identifier)
        {
            return IsPostgreSql ? Quote(identifier) : identifier;
        }

        private bool IsPostgreSql
        {
            get { return m_conn is NpgsqlConnection; }
        }

        private string NowExpression
        {
            get { return IsPostgreSql ? "CURRENT_TIMESTAMP" : "NOW()"; }
        }

        private string ResolveUsername(UserInformation userInfo)
        {
            if (userInfo == null)
                return "--UNKNOWN--";

            string username = Settings.GetUseModifiedName() ? userInfo.Username : userInfo.OriginalUsername;
            return string.IsNullOrWhiteSpace(username) ? "--UNKNOWN--" : username;
        }

        private string GetIpAddress()
        {
            foreach (IPAddress addr in Dns.GetHostAddresses(string.Empty))
                if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return addr.ToString();
            return "-INVALID IP ADDRESS-";
        }
    }
}


