# pGina - MySQL 8 / MariaDB Edition

[![GitHub release](https://img.shields.io/github/release/pedropablobm/pgina-mysql8.svg)](https://github.com/pedropablobm/pgina-mysql8/releases)
[![License](https://img.shields.io/badge/license-BSD%203--Clause-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-lightgrey.svg)](https://github.com/pedropablobm/pgina-mysql8)

## Overview

pGina is a pluggable Open Source Credential Provider (and GINA) replacement for Windows. Plugins are written in managed code and allow for user authentication, session management and login time actions.

This is a fork of the original [pGina](https://github.com/pgina/pgina) project, updated to support modern MySQL 8.x and MariaDB 10.x/11.x databases, and compatible with Windows 10 and Windows 11.

## Features

- **MySQL 8.x and MariaDB 10.x/11.x Support** - Full compatibility with modern MySQL and MariaDB servers
- **Windows 10/11 Compatible** - Tested on Windows 10 21H2+ and Windows 11 22H2+
- **Multiple Authentication Plugins** - MySQL, LDAP, LocalMachine, RADIUS
- **Session Management** - Drive mapping, session limits, logging
- **Group-based Authorization** - Flexible authorization rules based on database groups
- **Password Hash Support** - BCrypt, MD5, SHA-256, SHA-512
- **Active User Validation** - Can require a status column such as `estado=1`
- **TLS Options** - Supports `Required`, `VerifyCA`, and `VerifyFull`

## Requirements

### Operating System
- Windows 10 version 21H2 or later
- Windows 11 version 22H2 or later

### Runtime Dependencies
- .NET Framework 4.8 or later
- Visual C++ 2013 Redistributable (x86 and x64)

### Database Server
- MySQL 8.0 or later
- MariaDB 10.x or 11.x

## Installation

1. Download the latest release from [Releases](https://github.com/pedropablobm/pgina-mysql8/releases)
2. Run `pGinaSetup-4.0.0-MySQL8.exe` as Administrator
3. Follow the installation wizard
4. Configure pGina using the Configuration application

## Database Configuration

### MySQL 8.x / MariaDB Connection Settings

```text
Host: your-mysql-server (e.g., 192.168.1.100)
Port: 3306
Database: your_database_name
User: your_db_user
Password: your_db_password
Table: estudiantes (or your custom table name)
TLS Mode: None, Required, VerifyCA, or VerifyFull
```

### Required Table Structure

```sql
CREATE TABLE `estudiantes` (
  `id` int(20) NOT NULL AUTO_INCREMENT,
  `codigo` varchar(20) NOT NULL COMMENT 'Username for login',
  `nombre` varchar(100) NOT NULL COMMENT 'First name',
  `apellido` varchar(100) NOT NULL COMMENT 'Last name',
  `identificacion` varchar(15) NOT NULL COMMENT 'ID number',
  `direccion` varchar(200) NOT NULL COMMENT 'Email address',
  `estado` int(11) NOT NULL DEFAULT 1 COMMENT 'Status: 1=active, 0=inactive',
  `id_carrera` int(11) NOT NULL,
  `id_nivel` int(11) NOT NULL,
  `metodo_hash` text NOT NULL COMMENT 'Hash method: MD5, SHA256, SHA512',
  `clave` text DEFAULT NULL COMMENT 'Password hash',
  PRIMARY KEY (`id`)
);
```

The plugin can validate only active users by checking a configurable status column.
Default settings expect `estado = 1`.

### Password Hashing

The plugin supports multiple hash algorithms:

| Algorithm | `metodo_hash` value | Example Hash |
|-----------|---------------------|--------------|
| BCrypt | `BCRYPT` | `$2b$12$...` |
| MD5 | `MD5` | `e10adc3949ba59abbe56e057f20f883e` |
| SHA-256 | `SHA256` | `e3b0c44298fc1c149afbf4c8996fb924...` |
| SHA-512 | `SHA512` | `cf83e1357eefb8bdf1542850d66d8007...` |

## Plugin Configuration

### MySQL Authentication Plugin

In pGina Configuration:

1. Go to **Plugins** → **Authentication**
2. Enable **MySQL Auth** plugin
3. Configure connection settings:
   - **Host**: MySQL server address
   - **Port**: 3306 (default)
   - **TLS**: `VerifyFull` for inter-campus deployments when certificates are available
   - **Database**: Database name
   - **User**: Database username
   - **Password**: Database password
   - **Table**: User table name (default: `estudiantes`)
   - **Username Column**: `codigo`
   - **Password Column**: `clave`
   - **Hash Method Column**: `metodo_hash`
   - **Require active user status filter**: enabled for academic environments
   - **Status Column**: `estado`
   - **Active Value**: `1`

4. Go to **Authorization** and configure rules
5. Save configuration

## Building from Source

### Prerequisites

- Visual Studio 2019 or 2022
- .NET Framework 4.8 SDK
- Inno Setup 6.x (for installer)

### Build Steps

```powershell
# Clone the repository
git clone https://github.com/pedropablobm/pgina-mysql8.git
cd pgina-mysql8

# Build with Visual Studio
# Open pGina-3.x.sln and build in Release mode for both x64 and Win32

# Or use MSBuild
msbuild pGina-3.x.sln /p:Configuration=Release /p:Platform=x64
msbuild pGina-3.x.sln /p:Configuration=Release /p:Platform=Win32

# Create installer
# Open Installer/installer.iss with Inno Setup and compile
```

## Troubleshooting

### Common Issues

1. **Login screen doesn't appear**
   - Ensure Visual C++ 2013 Redistributable (x86 and x64) is installed
   - Run `pGina.InstallUtil.exe post-install` as Administrator
   - Check Windows Event Viewer for errors

2. **Cannot connect to MySQL 8.x**
   - Verify the user has proper permissions
   - Check that MySQL is configured to accept connections from your IP
   - Ensure the authentication plugin is `mysql_native_password` or `caching_sha2_password`
   - If using TLS between campuses, verify certificates match the selected TLS mode

3. **Authentication fails**
   - Verify the password hash matches the stored hash
   - Check the `metodo_hash` column value matches your configuration
   - Verify the user's `estado` field is set to 1 (active)

### Logs Location

- Windows Event Viewer → Application → pGina
- Log files: `C:\ProgramData\pGina\`

## Changes from Original pGina

| Feature | Original pGina | This Fork |
|---------|---------------|-----------|
| MySQL Connector | MySql.Data 6.5.4 | MySqlConnector 2.x |
| MySQL 8.x Support | ❌ | ✅ |
| MariaDB Support | Limited | Full |
| Windows 10 | Partial | Full |
| Windows 11 | ❌ | ✅ |
| .NET Framework | 4.0 | 4.8 |
| VC++ Runtime | 2012 | 2013 |

## License

BSD 3-Clause License. See [LICENSE](LICENSE) for details.

## Credits

- Original pGina Team - https://github.com/pgina/pgina
- MySqlConnector contributors - https://github.com/mysqlconnector/net

## Contributing

Contributions are welcome! Please read the contributing guidelines before submitting pull requests.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add some amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## Support

- **Issues**: [GitHub Issues](https://github.com/pedropablobm/pgina-mysql8/issues)
- **Original Project**: https://pgina.org
