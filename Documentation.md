# DatabaseGrinder - Database Replication Testing Tool

## System Purpose & Design

**Purpose**: A cross-platform console application designed to test and monitor database replication lag between a single primary PostgreSQL database and multiple replica instances. The tool provides real-time visualization of write operations on the primary database and tracks how quickly those changes propagate to configured replica databases managed by external replication systems.

**Core Architecture**:

- **Dual-pane console interface** with dynamic terminal size detection (minimum 20x20)
- **Left pane**: Primary database writer that continuously inserts timestamped records every 100ms and displays write operations
- **Right pane**: Multi-connection monitor showing replication status for up to 3 PostgreSQL replicas stacked vertically
- **EF Core** for database operations with migrations support (PostgreSQL 17+ only)
- **Multi-threaded design** with separate threads for UI rendering, database writing, and replication monitoring
- **Cross-platform compatibility** (Windows/Linux) using .NET 9.0
- **Automatic data cleanup** with 5-minute retention policy

**Key Technologies**:

- .NET 9.0 with .slnx solution format
- Entity Framework Core with PostgreSQL 17+ provider
- Nerdbank.GitVersioning for version management
- Console UI with color coding and dynamic layouts
- Multi-threading for concurrent operations
- Configuration via appsettings.json only

## Requirements Specification

Based on the clarified requirements, here are the confirmed specifications:

### Database Requirements

- **Single Primary Database**: PostgreSQL 17+ only
- **Database Management**: Writer user creates database and manages schema
- **User Management**: Writer user (superuser) creates reader user if not exists
- **Table Schema**: ID (auto-increment primary key) + Timestamp columns
- **Data Retention**: 5 minutes with automatic cleanup of older records
- **Write Frequency**: Every 100ms (10 times per second)
- **External Replication**: Application monitors replication, doesn't handle it

### UI & Display Requirements

- **Layout**: Dual-pane with vertical stacking on right side
- **Console Size**: Minimum 20x20 terminal support
- **Replica Count**: Maximum 3 replica connections
- **Lag Visualization**: Time-based and record-count lag display with color-coded status indicators
- **Connection Failure**: Visual indicators for failed replica connections (critical feature)

### Configuration Requirements

- **Configuration Source**: appsettings.json file only (no environment variables or command line)
- **Connection Format**: Standard PostgreSQL connection strings
- **Credential Types**: Read/write credentials for primary, read-only credentials for replicas

### Performance & Technical Requirements

- **Threading**: Separate threads for replication lag checks and UI updates
- **Update Frequency**: Optimized for remote SSH connections (~500ms-1s refresh rate)
- **Logging**: Console display only (no file-based logging)
- **Platform**: Cross-platform (Windows/Linux) support

## Implementation Phases

### Phase 1: Project Foundation & Setup
**Deliverables**: Working .NET 9.0 solution with basic console output
- Create .slnx solution with single .csproj targeting .NET 9.0
- Set up Nerdbank.GitVersioning configuration
- Configure basic EF Core with PostgreSQL provider
- Implement initial EF migration for timestamp table
- Create basic console application structure with dual-pane layout detection
- Verify cross-platform compatibility (Windows/Linux)

### Phase 2: Database Layer Implementation  
**Deliverables**: Complete database operations with credential management and user provisioning
- Implement database creation and user management service
- Add superuser connection validation and database creation logic
- Implement reader user creation and permission assignment
- Enhance EF Core DbContext with connection string management
- Create database models and migration for test data table
- Add basic database operations (insert, query latest records)
- Implement connection health checking for both writer and reader connections
- Add automated database setup workflow

### Phase 3: Console UI Framework
**Deliverables**: Dynamic console interface with resizing support
- Implement terminal size detection and resize handling
- Create dual-pane console layout system
- Develop color-coded display utilities
- Implement thread-safe console writing mechanisms
- Add keyboard input handling (if needed for configuration/exit)
- Create responsive layout system for different terminal sizes

### Phase 4: Left Pane - Database Writer
**Deliverables**: Continuous database writing with visual feedback
- Implement background thread for continuous timestamp insertion
- Create visual display of write operations on left pane
- Add write statistics (records/second, total records, errors)
- Implement configurable write frequency
- Add error handling and recovery for write operations
- Display connection status and write queue information

### Phase 5: Right Pane - Replication Monitor
**Deliverables**: Multi-connection replication lag monitoring
- Implement configurable replica connection management
- Create replication lag calculation logic
- Develop visual status indicators (colors, progress bars, metrics)
- Implement concurrent monitoring of multiple replicas
- Add detailed lag metrics (time behind, record count behind)
- Create visual hierarchy for multiple connection displays

### Phase 6: Configuration & Polish
**Deliverables**: Production-ready configuration and user experience
- Implement configuration file system for replica connections
- Add command-line argument support
- Enhance error handling and recovery mechanisms
- Implement proper logging and diagnostics
- Add startup validation and connection testing
- Create user documentation and usage examples

### Phase 7: Testing & Optimization
**Deliverables**: Tested, optimized, and documented application
- Performance testing and optimization
- Cross-platform compatibility verification
- Load testing with multiple replicas
- Memory and CPU usage optimization
- Documentation and deployment guides
- Error scenario testing and recovery validation

## Technical Implementation Details

### Project Structure:
```
DatabaseGrinder/
├── DatabaseGrinder.slnx
├── src/
│   └── DatabaseGrinder/
│       ├── DatabaseGrinder.csproj
│       ├── Program.cs
│       ├── Models/
│       │   └── TestRecord.cs
│       ├── Data/
│       │   ├── DatabaseContext.cs
│       │   └── Migrations/
│       ├── Services/
│       │   ├── DatabaseWriter.cs
│       │   ├── ReplicationMonitor.cs
│       │   └── ConsoleManager.cs
│       ├── Configuration/
│       │   └── ConnectionSettings.cs
│       └── UI/
│           ├── LeftPane.cs
│           └── RightPane.cs
├── version.json (Nerdbank.GitVersioning)
└── appsettings.json
```

### Key Dependencies

- **Microsoft.EntityFrameworkCore** (9.0+)
- **Npgsql.EntityFrameworkCore.PostgreSQL** (9.0+) - PostgreSQL 17+ support
- **Nerdbank.GitVersioning**
- **Microsoft.Extensions.Configuration** - appsettings.json support
- **Microsoft.Extensions.Hosting**
- **Microsoft.Extensions.Logging** - console logging only

### Threading Model

1. **Main Thread**: UI rendering and input handling
2. **Setup Thread**: Database and user creation during startup
3. **Writer Thread**: Continuous database writing every 100ms
4. **Monitor Thread(s)**: Separate thread per replica for lag monitoring (up to 3)
5. **UI Update Thread**: Console refresh optimized for SSH connections (~500ms-1s)
6. **Cleanup Thread**: Periodic cleanup of records older than 5 minutes

### Database Management Components

**DatabaseSetupService:** Handles initial database and user creation
- Validates superuser connection
- Creates database if missing
- Creates reader user with appropriate permissions
- Validates reader user connectivity

**SQL Commands for User Management:**
```sql
-- Create database (if not exists)
CREATE DATABASE grinder_primary;

-- Create reader role (if not exists)  
CREATE ROLE "DatabaseGrinderReader" WITH LOGIN PASSWORD 'readpass';

-- Grant permissions to reader
GRANT CONNECT ON DATABASE grinder_primary TO "DatabaseGrinderReader";
GRANT USAGE ON SCHEMA public TO "DatabaseGrinderReader";
GRANT SELECT ON ALL TABLES IN SCHEMA public TO "DatabaseGrinderReader";
GRANT SELECT ON ALL SEQUENCES IN SCHEMA public TO "DatabaseGrinderReader";

-- Grant future table permissions
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO "DatabaseGrinderReader";
```

### Configuration Structure (appsettings.json)

```json
{
  "DatabaseGrinder": {
    "PrimaryConnection": {
      "ConnectionString": "Host=localhost;Database=grinder_primary;Username=DatabaseGrinder;Password=DatabaseGrinder"
    },
    "ReplicaConnections": [
      {
        "Name": "Replica 1",
        "ConnectionString": "Host=localhost;Database=grinder_primary;Username=DatabaseGrinderReader;Password=readpass"
      },
      {
        "Name": "Replica 2", 
        "ConnectionString": "Host=localhost;Database=grinder_primary;Username=DatabaseGrinderReader;Password=readpass"
      },
      {
        "Name": "Replica 3",
        "ConnectionString": "Host=localhost;Database=grinder_primary;Username=DatabaseGrinderReader;Password=readpass"
      }
    ],
    "DatabaseManagement": {
      "AutoCreateDatabase": true,
      "AutoCreateUsers": true,
      "ReaderUsername": "DatabaseGrinderReader",
      "ReaderPassword": "readpass"
    },
    "Settings": {
      "WriteIntervalMs": 100,
      "DataRetentionMinutes": 5,
      "UIRefreshIntervalMs": 800,
      "MinConsoleWidth": 20,
      "MinConsoleHeight": 20
    }
  }
}
```

### Database Management Workflow

**Superuser Setup Requirements:**
- Primary connection user (`DatabaseGrinder`) must have superuser privileges
- Can create databases, roles, and grant permissions
- Will automatically create database if it doesn't exist
- Will create reader user (`DatabaseGrinderReader`) if not present

**Automated Setup Process:**
1. Connect with superuser credentials
2. Create database `grinder_primary` if not exists
3. Create reader role `DatabaseGrinderReader` if not exists
4. Grant appropriate permissions to reader role
5. Run EF Core migrations to create tables
6. Verify reader user can connect and read data

### Cross-Platform Considerations:
- Use `Environment.OSVersion` for platform-specific console behaviors
- ANSI color codes for cross-platform terminal colors
- Path handling with `Path.Combine()` for file operations
- Console size detection using `Console.WindowWidth/Height`

## Summary

This plan provides a comprehensive roadmap for building DatabaseGrinder as a focused database replication monitoring tool. The phased approach ensures we build a solid foundation before adding complex features, with each phase delivering working functionality.

**Key Design Decisions Finalized:**

- **PostgreSQL 17+ only** - Single technology stack for consistency
- **High-frequency writes** - 100ms intervals for precise replication lag testing
- **Visual failure detection** - Critical feature for monitoring replication health
- **SSH-optimized UI** - Refresh rates suitable for remote connections
- **Automatic cleanup** - 5-minute data retention with background cleanup
- **Simple configuration** - Single appsettings.json file approach
- **Thread-per-replica** - Separate monitoring threads for accurate lag detection
- **Minimal console support** - 20x20 minimum for embedded/constrained environments

**Technical Highlights:**

- **Real-time monitoring** with 100ms write precision
- **Color-coded status indicators** for immediate visual feedback
- **Connection failure detection** as primary use case
- **Cross-platform compatibility** for diverse deployment scenarios
- **Optimized for remote access** via SSH connections

**Implementation Status:**

✅ **Phase 1 Complete: Project Foundation & Setup**
- .slnx solution with .NET 9.0 console project
- Nerdbank.GitVersioning configuration
- EF Core with PostgreSQL 17+ support and migrations
- Cross-platform console UI framework with dual-pane layout
- Configuration system with validation
- Basic application structure with dependency injection

**Next Steps:**

Phase 1 has been successfully implemented and tested. The application demonstrates:
- Proper configuration loading and validation
- Console UI initialization with size detection
- Database migration system (ready for PostgreSQL connection)
- Cross-platform compatibility
- Professional error handling and logging

Ready to proceed with Phase 2: Database Layer Implementation to add the database writing and monitoring services.