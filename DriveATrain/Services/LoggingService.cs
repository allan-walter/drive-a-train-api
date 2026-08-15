namespace DriveATrain.Services;

public class LoggingService(DccService dccService)
{
    public async Task ThrowExceptionAndStop(Exception e)
    {
        // Very important, so the train doesn't run away
        await dccService.PowerOff();
        
        throw e;
    }
}