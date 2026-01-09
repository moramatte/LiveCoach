using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FakeItEasy;
using Infrastructure;
using Infrastructure.Speed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VasaLiveFeeder;
using VasaLiveFeeder.LiveScraper;

namespace VasaLiveFeeder.Tests;

[TestClass]
public class Function1Tests
{
    [TestInitialize]
    public void Setup()
    {
        BootstrapperForLiveFeeder.Reset();
        ServiceLocator.RegisterTransient<ILiveScraper, TestableLiveScraper>();
        TestableLiveScraper.ReturnValue = 60;
    }

    [TestMethod]
    public async Task Run_ReturnsCoordinates_FromQuery()
    {
        var func = new Function1(NullLogger<Function1>.Instance);
        var req = new TestHttpRequestData(new Uri("http://localhost?raceName=Vasaloppet&km=14.67"), "GET", "");

        var resp = await func.Run(req);

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = ((TestHttpResponseData)resp).GetBodyAsString();
        var obj = JsonSerializer.Deserialize<JsonElement>(body);
        Assert.AreEqual("Vasaloppet", obj.GetProperty("raceName").GetString());
        Assert.AreEqual(14.67, obj.GetProperty("progressKm").GetDouble());
    }

    [TestMethod]
    public async Task BehindDistCanBeDerived()
    {
        var func = new Function1(NullLogger<Function1>.Instance);

        var delta = await func.DeriveDistanceDelta("Vasaloppet", 30.00);
        Assert.AreEqual(15.00, delta);

        delta = await func.DeriveDistanceDelta("Vasaloppet", 60.00);
        Assert.AreEqual(-30, delta);

        delta = await func.DeriveDistanceDelta("Vasaloppet", 40.00);
        Assert.AreEqual(0, delta);
    }

    [TestMethod]
    public async Task DeltaCanBeDerived()
    {
        var func = new Function1(NullLogger<Function1>.Instance);

        var speed15 = Speed.FromKilometersPerHour(15);
        var speed18_75 = Speed.FromKilometersPerHour(18.75);

        var delta = await func.DeriveTempoDelta("Vasaloppet", "40.00", "15.00");
        Assert.AreEqual(speed15.KilometersPerHour, delta);

        delta = await func.DeriveTempoDelta("Vasaloppet", "30.00", "15.00");
        Assert.AreEqual(speed18_75.KilometersPerHour, delta);
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
