using System.Net;
using System.Text.Json;
using Netch.Controllers;
using Netch.Interfaces;
using Netch.Models;

namespace Netch.Servers;

public class V2rayController : Guard, IServerController
{
    public V2rayController() : base("xray.exe")
    {
        //if (!Global.Settings.V2RayConfig.XrayCone)
        //    Instance.StartInfo.Environment["XRAY_CONE_DISABLED"] = "true";
    }

    // Xray uses slightly different startup message
    protected override IEnumerable<string> StartedKeywords => new[] { "started" };

    protected override IEnumerable<string> FailedKeywords => new[] { "config file not readable", "failed to" };

    public override string Name => "Xray";

    public ushort? Socks5LocalPort { get; set; }

    public string? LocalAddress { get; set; }
    public string? OutboundInterface { get; set; }

    public virtual async Task<Socks5Server> StartAsync(Server s)
    {
        await using (var fileStream = new FileStream(Constants.TempConfig, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            var config = await XrayProfileConfig.GenerateAsync(s, this.Socks5LocalPort(), LocalAddress ?? Global.Settings.LocalAddress);
            XrayProfileConfig.BindOutboundInterface(config, OutboundInterface);
            await JsonSerializer.SerializeAsync(fileStream, config, Global.NewCustomJsonSerializerOptions());
        }

        await StartGuardAsync("run -c ..\\data\\last.json");
        return new Socks5Server(IPAddress.Loopback.ToString(), this.Socks5LocalPort(), s.Hostname);
    }
}
