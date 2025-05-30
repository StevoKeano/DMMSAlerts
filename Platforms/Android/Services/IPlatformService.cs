namespace AviationApp.Services;

public interface IPlatformService
{
    Task ShowPermissionPopupAndRequestAsync();
    Task<bool> ArePermissionsGrantedAsync();
}