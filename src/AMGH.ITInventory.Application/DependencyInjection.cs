using AMGH.ITInventory.Application.Assets;
using AMGH.ITInventory.Application.Assignments;
using AMGH.ITInventory.Application.Labels;
using AMGH.ITInventory.Application.Lookups;
using AMGH.ITInventory.Application.MasterData;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace AMGH.ITInventory.Application;

public static class DependencyInjection
{
    /// <summary>Registers application services (use cases, validators, master-data definitions).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IValidator<AssetEditModel>, AssetEditValidator>();
        services.AddScoped<ILookupService, LookupService>();
        services.AddScoped<IAssetService, AssetService>();
        services.AddScoped<IAssetDocumentService, AssetDocumentService>();
        services.AddScoped<IAssetImportExportService, AssetImportExportService>();
        services.AddScoped<IAssignmentService, AssignmentService>();
        services.AddScoped<Dashboard.IDashboardService, Dashboard.DashboardService>();
        services.AddScoped<Search.ISearchService, Search.SearchService>();
        services.AddScoped<Documents.IDocumentNumbers, Documents.DocumentNumbers>();
        services.AddScoped<Documents.IDocumentService, Documents.DocumentService>();
        services.AddScoped<MasterData.IMasterDataImportService, MasterData.MasterDataImportService>();
        // asset configuration (Release 2.9)
        services.AddScoped<AssetConfiguration.INumberingService, AssetConfiguration.NumberingService>();
        services.AddScoped<AssetConfiguration.IStarterCategoryService, AssetConfiguration.StarterCategoryService>();
        // notifications (Release 2.5)
        services.AddScoped<Notifications.INotificationSettingsService, Notifications.NotificationSettingsService>();
        services.AddScoped<Notifications.INotificationService, Notifications.NotificationService>();
        services.AddScoped<Notifications.INotificationScheduler, Notifications.NotificationScheduler>();
        services.AddScoped<Notifications.IMailQueue, Notifications.MailQueue>();
        services.AddScoped<Views.ISavedViewService, Views.SavedViewService>();
        // asset lifecycle (Release 2.3)
        services.AddScoped<Lifecycle.IApprovalService, Lifecycle.ApprovalService>();
        services.AddScoped<CompanyAdmin.ICompanyDataExportService, CompanyAdmin.CompanyDataExportService>();
        services.AddScoped<Lifecycle.IMovementService, Lifecycle.MovementService>();
        services.AddScoped<Lifecycle.IDisposalService, Lifecycle.DisposalService>();
        services.AddScoped<Lifecycle.IMaintenanceService, Lifecycle.MaintenanceService>();
        services.AddScoped<Lifecycle.IVerificationService, Lifecycle.VerificationService>();
        services.AddScoped<Lifecycle.IWarrantyService, Lifecycle.WarrantyService>();
        services.AddScoped<Lifecycle.ILicenseService, Lifecycle.LicenseService>();
        // reports: stateless definitions (shared) + per-request service
        services.AddSingleton<Reports.IReportSource, Reports.AssetRegisterReport>();
        services.AddSingleton<Reports.IReportSource, Reports.AssignmentReport>();
        services.AddSingleton<Reports.IReportSource, Reports.WarrantyReport>();
        services.AddSingleton<Reports.IReportSource, Reports.RiskReport>();
        services.AddSingleton<Reports.IReportSource, Reports.ComplianceReport>();
        services.AddSingleton<Reports.IReportSource, Reports.MaintenanceReport>();
        services.AddSingleton<Reports.IReportSource, Reports.MovementReport>();
        services.AddSingleton<Reports.IReportSource, Reports.DisposalReport>();
        services.AddSingleton<Reports.IReportSource, Reports.LicenseReport>();
        services.AddSingleton<Reports.IReportSource, Reports.AuditReport>();
        services.AddScoped<Reports.IReportService, Reports.ReportService>();
        services.AddScoped<ILabelService, LabelService>();

        // Order here is the order of the Master data menu.
        services.AddSingleton<IMasterDataDefinition, EmployeeDefinition>();
        services.AddSingleton<IMasterDataDefinition, DepartmentDefinition>();
        services.AddSingleton<IMasterDataDefinition, LocationDefinition>();
        services.AddSingleton<IMasterDataDefinition, BuildingDefinition>();
        services.AddSingleton<IMasterDataDefinition, FloorDefinition>();
        services.AddSingleton<IMasterDataDefinition, RoomDefinition>();
        services.AddSingleton<IMasterDataDefinition, VendorDefinition>();
        services.AddSingleton<IMasterDataDefinition, ManufacturerDefinition>();
        services.AddSingleton<IMasterDataDefinition, CategoryDefinition>();
        services.AddSingleton<IMasterDataDefinition, SubCategoryDefinition>();
        services.AddScoped<IMasterDataService, MasterDataService>();
        // multi-tenancy: provisioning of customer databases
        services.AddScoped<Tenancy.ITenantProvisioningService, Tenancy.TenantProvisioningService>();
        return services;
    }
}
