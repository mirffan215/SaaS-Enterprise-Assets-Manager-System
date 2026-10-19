using AMGH.ITInventory.Application.Abstractions.QrCodes;
using AMGH.ITInventory.Application.Abstractions.Spreadsheets;
using AMGH.ITInventory.Application.Abstractions.Storage;
using AMGH.ITInventory.Infrastructure.Barcodes;
using AMGH.ITInventory.Infrastructure.QrCodes;
using AMGH.ITInventory.Infrastructure.Spreadsheets;
using AMGH.ITInventory.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AMGH.ITInventory.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.AddScoped<IFileStorage, LocalFileStorage>(); // scoped: resolves the request's company
        services.AddSingleton<ISpreadsheetWriter, XlsxWriter>();
        services.AddSingleton<ISpreadsheetReader, XlsxReader>();
        services.AddSingleton<IQrCodeGenerator, QrCodeGenerator>();
        services.AddSingleton<IBarcodeGenerator, BarcodeGenerator>();
        services.AddSingleton<Application.Abstractions.Documents.IWordReportWriter, Documents.DocxReportWriter>();
        services.AddSingleton<Mail.IOAuthTokenProvider, Mail.EntraTokenProvider>();
        services.AddSingleton<Application.Abstractions.Mail.IMailSender, Mail.SmtpMailSender>();
        return services;
    }
}
