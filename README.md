# DatabaseGrinder

A high-performance PostgreSQL replication monitoring and database stress testing tool built with .NET 9.0. DatabaseGrinder provides real-time monitoring of database write operations and PostgreSQL replication lag across multiple replica nodes.

## 🚀 Features

- **Continuous Database Writing**: Writes timestamp records every 100ms to stress test database performance
- **Real-time Statistics**: Live monitoring of write operations, throughput, and error rates
- **Dual-Pane Console UI**: Split-screen interface showing writer status and replication monitoring
- **PostgreSQL Replication Monitoring**: Track replication lag across multiple replica connections
- **Automatic Database Setup**: Creates databases and users automatically with proper permissions
- **Data Retention Management**: Automatic cleanup of records older than 5 minutes
- **Robust Error Handling**: Comprehensive error recovery and connection resilience
- **Background Services**: Non-blocking operations with graceful shutdown capabilities

## 🏗️ Architecture

```text
DatabaseGrinder/
├── src/DatabaseGrinder/
│   ├── Data/
│   │   ├── DatabaseContext.cs      # Entity Framework Core context
│   │   └── Models/
│   │       └── TestRecord.cs       # Database model for test records
│   ├── Services/
│   │   ├── DatabaseSetupService.cs # Automatic DB/user provisioning
│   │   ├── DatabaseWriter.cs       # Continuous write operations
│   │   └── UIManager.cs            # Console UI management
│   ├── UI/
│   │   ├── ConsoleManager.cs       # Console layout management
│   │   ├── LeftPane.cs            # Database writer display
│   │   └── RightPane.cs           # Replication monitor display
│   ├── Configuration/
│   │   └── DatabaseGrinderSettings.cs # Application settings
│   └── Program.cs                  # Application entry point
└── README.md
```

## 📋 Requirements

- **.NET 9.0 SDK** or later
- **PostgreSQL 17+** server with superuser access
- **Windows/Linux/macOS** (cross-platform compatible)
- **Console window** minimum 140x30 characters for optimal UI display

## ⚙️ Configuration

Configure the application via `appsettings.json`:

```json
{
  "DatabaseGrinderSettings": {
    "DatabaseName": "DatabaseGrinder",
    "WriteConnectionString": "Host=localhost;Database=DatabaseGrinder;Username=grinder_writer;Password=writer123!;",
    "ReadConnectionStrings": [
      "Host=replica1.example.com;Database=DatabaseGrinder;Username=grinder_reader;Password=reader123!;",
      "Host=replica2.example.com;Database=DatabaseGrinder;Username=grinder_reader;Password=reader123!;",
      "Host=replica3.example.com;Database=DatabaseGrinder;Username=grinder_reader;Password=reader123!;"
    ],
    "SuperuserConnectionString": "Host=localhost;Database=postgres;Username=postgres;Password=your_postgres_password;",
    "WriteIntervalMs": 100,
    "DataRetentionMinutes": 5,
    "UIRefreshIntervalMs": 1000
  }
}
```

### Configuration Properties

| Property | Description | Default |
|----------|-------------|---------|
| `DatabaseName` | Target database name | `DatabaseGrinder` |
| `WriteConnectionString` | Primary database connection for writes | Required |
| `ReadConnectionStrings` | Array of replica connections to monitor | Optional |
| `SuperuserConnectionString` | PostgreSQL superuser connection for setup | Required |
| `WriteIntervalMs` | Interval between database writes (milliseconds) | `100` |
| `DataRetentionMinutes` | How long to keep test records | `5` |
| `UIRefreshIntervalMs` | Console UI refresh rate | `1000` |

## 🚀 Quick Start

1. **Clone the repository**:

   ```bash
   git clone https://github.com/panoramicdata/DatabaseGrinder.git
   cd DatabaseGrinder
   ```

2. **Configure your database connections**:

   ```bash
   cp appsettings.example.json appsettings.json
   # Edit appsettings.json with your PostgreSQL connection details
   ```

3. **Build and run**:

   ```bash
   cd src/DatabaseGrinder
   dotnet build
   dotnet run
   ```

4. **Monitor the output**:
   - **Left Pane**: Shows continuous database write operations and statistics
   - **Right Pane**: Displays replication lag for configured replica connections
   - Press `q` to quit gracefully

## 📊 Console Interface

```text
                     DATABASE WRITER                                             REPLICATION MONITOR
─────────────────────────────────────────────────────────── ───────────────────────────────────────────────────────────
[13:35:42.236] Record #302 inserted successfully          Replica 1: LAG 0.05s
[13:35:42.345] Record #303 inserted successfully          
[13:35:42.454] Record #304 inserted successfully          Last: 13:35:42
[13:35:42.561] Record #305 inserted successfully
Stats: 310/sec | Total: 310 | Errors: 0 | Up: 00:00:33   ···································
                                                          Replica 2: LAG 0.12s
                                                          
                                                          Last: 13:35:41
                                                          
                                                          
                                                          ···································
                                                          Replica 3: DISCONNECTED
                                                          
                                                          Last: Never
─────────────────────────────────────────────────────────
Status: Database Writer Active
Press 'q' to quit | F5 to refresh
```

## 🔧 Development

### Building from Source

```bash
# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Run tests (when available)
dotnet test

# Create release build
dotnet build -c Release
```

### Project Structure

- **Entity Framework Core**: Database operations and migrations
- **Background Services**: `IHostedService` implementation for continuous operations  
- **Console UI Framework**: Custom dual-pane layout with real-time updates
- **Configuration System**: Strongly-typed settings with validation
- **Dependency Injection**: Full DI container setup with service lifetime management

## 📈 Performance Characteristics

- **Write Throughput**: ~10 records/second (configurable via `WriteIntervalMs`)
- **Memory Usage**: Minimal footprint with automatic data cleanup
- **CPU Usage**: Low impact background processing
- **Database Impact**: Lightweight timestamp-only records with automatic cleanup
- **UI Responsiveness**: Non-blocking console updates at 1Hz refresh rate

## 🔐 Security Notes

- Uses separate read/write database users with minimal required permissions
- Automatic user provisioning with secure password policies
- Connection string validation and sanitization
- No sensitive data stored in test records (timestamps only)

## 🐛 Troubleshooting

### Common Issues

1. **Database Connection Failed**:
   - Verify PostgreSQL server is running and accessible
   - Check connection strings in `appsettings.json`
   - Ensure superuser credentials are correct

2. **Permission Denied**:
   - Verify superuser connection has database creation privileges
   - Check firewall and PostgreSQL `pg_hba.conf` settings

3. **UI Display Issues**:
   - Ensure console window is at least 140x30 characters
   - Try running with `--no-build` flag if build issues occur

### Logging

DatabaseGrinder uses structured logging via Microsoft.Extensions.Logging. Logs include:

- Database operation status and timing
- Connection health and errors  
- UI component lifecycle events
- Performance statistics and metrics

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## 📞 Support

For support and questions:

- Create an issue in this repository
- Contact the development team at [support@panoramicdata.com](mailto:support@panoramicdata.com)

## 🏷️ Version History

- **v1.0.0** - Initial release with core database writing functionality
- **v1.1.0** - Added replication monitoring capabilities  
- **v1.2.0** - Enhanced UI and performance improvements

---

Built with ❤️ by the PanoramicData team using .NET 9.0 and PostgreSQL 17+