using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using MySqlConnector;

namespace pGina.Plugin.MySqlLogger
{
    interface ILoggerMode
    {
        //Logs the specified event/properties
        bool Log(System.ServiceProcess.SessionChangeDescription changeDescription, pGina.Shared.Types.SessionProperties properties);

        //Tests to make sure the table exists, and contains the right columns, returns a string indicating the table status.
        string TestTable();

        //Attempts to create the neccesary table for the logging mode, and returns a string indicating it's success/failure
        string CreateTable();

        //Sets the connection to the MySql server, so that multiple loggers can share one stream
        void SetConnection(MySqlConnection m_conn);
    }

    class LoggerModeFactory
    {

        static private MySqlConnection m_conn = null;

        private LoggerModeFactory() { }

        public static ILoggerMode getLoggerMode(LoggerMode mode)
        {
            //Create a new MySqlConnection if no viable one is available
            if (m_conn == null || m_conn.State != System.Data.ConnectionState.Open)
            {
                string connStr = BuildConnectionString();
                m_conn = new MySqlConnection(connStr);
            }

            ILoggerMode logger = null;
            if (mode == LoggerMode.EVENT)
                logger = new EventLogger();
            else if (mode == LoggerMode.SESSION)
                logger = new SessionLogger();
            else
                throw new ArgumentException("Invalid LoggerMode");

            logger.SetConnection(m_conn);
            return logger;


        }

        public static void closeConnection()
        {
            if (m_conn != null)
                m_conn.Close();
            m_conn = null;
        }

        public static MySqlConnection CreateConnection()
        {
            return new MySqlConnection(BuildConnectionString());
        }

        private static string BuildConnectionString()
        {
            MySqlConnectionStringBuilder bldr = new MySqlConnectionStringBuilder();
            bldr.Server = Settings.Store.Host;
            bldr.Port = Settings.GetPort();
            bldr.UserID = Settings.Store.User;
            bldr.Database = Settings.Store.Database;
            bldr.Password = Settings.Store.GetEncryptedSetting("Password");

            return bldr.ConnectionString;
        }
    }
}
