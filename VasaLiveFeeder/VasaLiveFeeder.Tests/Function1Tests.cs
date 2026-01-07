using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VasaLiveFeeder;

namespace VasaLiveFeeder.Tests;

[TestClass]
public class Function1Tests
{
    [TestMethod]
    public async Task Run_ReturnsCoordinates_FromQuery()
    {
        var func = new Function1(NullLogger<Function1>.Instance);
        var req = new TestHttpRequestData(new Uri("http://localhost?lat=52.5&lon=13.4"), "GET", "");

        var resp = await func.Run(req);

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = ((TestHttpResponseData)resp).GetBodyAsString();
        var obj = JsonSerializer.Deserialize<JsonElement>(body);
        Assert.AreEqual(52.5, obj.GetProperty("latitude").GetDouble());
        Assert.AreEqual(13.4, obj.GetProperty("longitude").GetDouble());
    }

    [TestMethod]
    public async Task Run_ReturnsBadRequest_WhenMissing()
    {
        var func = new Function1(NullLogger<Function1>.Instance);
        var req = new TestHttpRequestData(new Uri("http://localhost"), "POST", "");

        var resp = await func.Run(req);

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
