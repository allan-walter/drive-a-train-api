using MQTTnet;

namespace DriveATrain.Services;

public class MqttService : BackgroundService
{
    private readonly ILogger<MqttService> _log;
    private readonly Config _config;
    private readonly IMqttClient _client = new MqttClientFactory().CreateMqttClient();

    public MqttService(ILogger<MqttService> log, Config config)
    {
        _log = log;
        _config = config;
    }

    public bool IsConnected => _client.IsConnected;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _client.ApplicationMessageReceivedAsync += e =>
        {
            _log.LogInformation("MQTT {Topic}: {Payload}",
                e.ApplicationMessage.Topic, e.ApplicationMessage.ConvertPayloadToString());
            return Task.CompletedTask;
        };

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(_config.Mqtt.Host, _config.Mqtt.Port)
            .WithClientId($"driveatrain-{Environment.MachineName}")
            .Build();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_client.IsConnected)
                {
                    await _client.ConnectAsync(options, ct);
                    await _client.SubscribeAsync("driveatrain/#", cancellationToken: ct);
                    _log.LogInformation("MQTT connected to {Host}:{Port}", _config.Mqtt.Host, _config.Mqtt.Port);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "MQTT reconnect failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }

    public async Task<bool> PublishAsync(string topic, string payload, bool retain)
    {
        if (!_client.IsConnected)
        {
            _log.LogWarning("MQTT not connected, dropping {Topic}: {Payload}", topic, payload);
            return false;
        }

        try
        {
            await _client.PublishStringAsync(topic, payload, retain: retain);
            _log.LogInformation("MQTT published {Topic}: {Payload}", topic, payload);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "MQTT publish to {Topic} failed", topic);
            return false;
        }
    }
}