# pGina

[http://pgina.org](http://pgina.org)

**pGina** is a pluggable open-source Credential Provider (and GINA) replacement.  
Plugins are written in managed code and allow for user authentication, session management, and login-time actions.

## Fork 2026: MySQL/MariaDB Support

This fork includes updated support for **MySQL** and **MariaDB** authentication, tested and working on modern Windows versions.

## Compatibility

| Operating System       | Status        |
|------------------------|---------------|
| Windows 10 (64-bit)    | ✅ Compatible |
| Windows 11 (64-bit)    | ✅ Compatible |
| Windows 10 (32-bit)    | ❌ Not supported |
| Windows 11 (32-bit)    | ❌ Not supported |

| Database       | Status        |
|----------------|---------------|
| MySQL 8.x      | ✅ Compatible |
| MariaDB 10.x   | ✅ Compatible |
| MariaDB 11.x   | ✅ Compatible |

## Changes in This Fork

### MySQL Connector Migration
- Migrated from **MySql.Data** to **MySqlConnector** for better compatibility and performance.
- Updated **MySQLAuth** plugin to use the new connector.
- Updated **MySQLLogger** plugin with correct namespace.

### Installer Improvements
- Updated `installer.iss` to clean previous configurations on install.
- Fixed deprecated Inno Setup constants (`{pf}` → `{commonpf}`, `x64` → `x64compatible`).
- Added automatic cleanup of registry and configuration files on uninstall.

### Added Dependencies
- MySqlConnector 2.0.0  
- Microsoft.Extensions.Logging.Abstractions 8.0.0  
- System.Diagnostics.DiagnosticSource  
- System.Buffers  
- System.Numerics.Vectors  
- System.Runtime.CompilerServices.Unsafe  

### Compilation Fixes
- Fixed missing icon file in Configuration project.  
- Added `BouncyCastle.Crypto` package for LDAP plugin.  
- Fixed `ILoggerMode` interface implementation in MySQLLogger.  
- Fixed namespace inconsistencies between `MySql.Data.MySqlClient` and `MySqlConnector`.

## Requirements

- Windows 10/11 (64-bit)  
- .NET Framework 4.0 or higher  
- Visual C++ 2012 Redistributable  
- MySQL 8.x or MariaDB 10.x/11.x  

## Building
### Build Requirements
- Visual Studio 2019 or higher
- Inno Setup 6.x

## Build Steps
- Open pgina-mysql8.sln in Visual Studio.
- Restore NuGet packages: Tools > NuGet Package Manager > Restore NuGet Packages.
- Rebuild the solution: Build > Rebuild Solution.
- Generate installer with Inno Setup: open Installer/installer.iss and compile.

## Installation
- Uninstall any previous versions of pGina.
- Run pGinaSetup-4.0.0.0.exe as Administrator.
- Configure the MySQL plugin in the configuration application.
- Test with the “Simulate” button before applying changes.

## MySQL Plugin Configuration
- Open pGina.Configuration.exe as Administrator.
- Go to Plugin Selection tab.
- Enable MySQL/MariaDB Auth in Authentication.
- Configure connection parameters:
-- Host: MySQL/MariaDB server
-- Port: Port number (default: 3306)
-- Database: Database name
-- User: Database user
-- Password: Database password
-- Table: Table containing users
- Save configuration.
- Use Simulate to test.

## Included Plugins
| Plugin	            |   Description    |
|---------------------|------------------|
| MySQL/MariaDB Auth	| Authentication against MySQL/MariaDB database |
| MySQL Logger	      | Event and session logging to MySQL/MariaDB |
| Local Machine	      | Windows local authentication |
| LDAP	              | LDAP/Active Directory authentication |

## Troubleshooting
Error: "Cannot load Microsoft.Extensions.Logging.Abstractions"
- Verify the DLL file exists in C:\Program Files\pGina\Plugins\Core\
- Reinstall the software

## Warning: "32-bit CredentialProvider is not registered"
- This warning is normal on 64-bit systems.
- The 64-bit CredentialProvider works correctly.
- This message can be safely ignored.

## Authentication fails
- Verify database connection.
- Verify user exists in configured table.
- Check password hash format (MD5, SHA256, etc.).
- Check logs in Show Log tab.

## Credits
- Original project: pGina Team

## Fork with updated MySQL/MariaDB support

## License
- See LICENSE file for details.
