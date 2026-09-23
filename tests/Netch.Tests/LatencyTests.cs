using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Netch.Application;

namespace Tests;

[TestClass]
public class LatencyTests
{
    [TestMethod]
    public async Task ClosedTcpPort_IsFailureInsteadOfTimeoutAsLatencyAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        Assert.IsTrue(await Netch.Utils.Utils.TCPingAsync(IPAddress.Loopback, port, 1000) < 0);
    }

    [TestMethod]
    public async Task HttpLatency_UsesSocksProxyAndWaitsForHttpResponseAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var exchange = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(deadline.Token);
            using var stream = client.GetStream();
            var greeting = new byte[2];
            await stream.ReadExactlyAsync(greeting, deadline.Token);
            await stream.ReadExactlyAsync(new byte[greeting[1]], deadline.Token);
            await stream.WriteAsync(new byte[] { 5, 0 }, deadline.Token);
            var request = new byte[5];
            await stream.ReadExactlyAsync(request, deadline.Token);
            Assert.AreEqual(3, (int)request[3]); // Remote DNS: no local lookup of example.invalid.
            var host = new byte[request[4]];
            await stream.ReadExactlyAsync(host, deadline.Token);
            Assert.AreEqual("example.invalid", Encoding.ASCII.GetString(host));
            await stream.ReadExactlyAsync(new byte[2], deadline.Token);
            await stream.WriteAsync(new byte[] { 5, 0, 0, 1, 127, 0, 0, 1, 0, 80 }, deadline.Token);
            var buffer = new byte[1024];
            var count = await stream.ReadAsync(buffer, deadline.Token);
            StringAssert.Contains(Encoding.ASCII.GetString(buffer, 0, count), "GET /latency");
            await Task.Delay(70, deadline.Token);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 204 No Content\r\nConnection: close\r\n\r\n"), deadline.Token);
        }, deadline.Token);
        var latency = await ServerLatencyService.MeasureThroughProxyAsync(new Uri($"socks5://127.0.0.1:{port}"), "http://example.invalid/latency", deadline.Token);
        await exchange;
        Assert.IsTrue(latency >= 60, $"HTTP response time was not measured: {latency}");
    }
}
