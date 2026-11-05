global using MicroKit.Messaging;
global using MicroKit.Messaging.EntityFrameworkCore;
global using MicroKit.Messaging.IntegrationTests.Infrastructure;
global using Microsoft.Data.Sqlite;
global using Microsoft.EntityFrameworkCore;
global using Shouldly;
global using Xunit;
global using MicroKit.Messaging.Spi;
global using MicroKit.Messaging.Processing;   // internal drain seams via InternalsVisibleTo (#185, removed by #135)
