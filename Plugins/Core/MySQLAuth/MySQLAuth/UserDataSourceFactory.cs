using System;

namespace pGina.Plugin.MySQLAuth
{
    static class UserDataSourceFactory
    {
        public static IUserDataSource Create()
        {
            switch (Settings.GetDatabaseProvider())
            {
                case Settings.DatabaseProvider.MySql:
                    return new MySqlUserDataSource();
                case Settings.DatabaseProvider.PostgreSql:
                    throw new NotSupportedException("PostgreSQL provider is not implemented yet.");
                default:
                    throw new NotSupportedException("Unsupported database provider.");
            }
        }
    }
}
