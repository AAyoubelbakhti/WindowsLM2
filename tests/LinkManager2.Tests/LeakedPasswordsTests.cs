using System.Diagnostics;
using System.Net;
using LinkManager2.Data;
using Xunit;

namespace LinkManager2.Tests;

public class LeakedPasswordsTests
{
    private const string Prefix = "5BAA6";
    private const string Rest = "1E4C9B93F3F0682250B6CF8331B7EE68FD8";
    private const string OtherRest = "0000000000000000000000000000000000A";
    private const string SupabaseUrl = "https://fixture.invalid";

    private sealed class FakeService(Func<CancellationToken, Task<HttpResponseMessage>> answer)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string?> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            Bodies.Add(
                request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken)
            );
            return await answer(cancellationToken);
        }
    }

    /// <summary>A body whose first bytes never come.</summary>
    private sealed class SilentStream : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    private static FakeService Answering(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) })
        );

    private static Task<long> TimesLeaked(FakeService service, string password = "password") =>
        LeakedPasswords.TimesLeakedAsync(password, SupabaseUrl, new HttpClient(service));

    private static async Task AssertLetThroughAtTheLimit(FakeService service)
    {
        var clock = Stopwatch.StartNew();
        var times = await TimesLeaked(service).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, times);
        Assert.InRange(clock.ElapsedMilliseconds, 2900, 10_000);
    }

    [Fact]
    public async Task SaysHowManyTimesAPasswordAppears() =>
        Assert.Equal(
            52256179,
            await TimesLeaked(Answering($"{OtherRest}:3\r\n{Rest}:52256179\r\n"))
        );

    [Fact]
    public async Task ReadsACountTooLargeForAnInt() =>
        Assert.Equal(3000000000, await TimesLeaked(Answering($"{Rest}:3000000000\r\n")));

    [Theory]
    [InlineData($"{OtherRest}:3\r\n")]
    [InlineData("")]
    public async Task APasswordNotInTheListCountsZero(string body) =>
        Assert.Equal(0, await TimesLeaked(Answering(body)));

    [Fact]
    public async Task IgnoresThePaddingEntries() =>
        Assert.Equal(0, await TimesLeaked(Answering($"{OtherRest}:3\r\n{Rest}:0\r\n")));

    [Fact]
    public async Task SendsOnlyTheStartOfTheHashToTheFunction()
    {
        var service = Answering("");
        await TimesLeaked(service);
        var request = Assert.Single(service.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            $"{SupabaseUrl}/functions/v1/password-range",
            request.RequestUri?.ToString()
        );
        Assert.Equal(Prefix, Assert.Single(service.Bodies));
        Assert.Null(request.Headers.Authorization);
        Assert.False(request.Headers.Contains("Cookie"));
    }

    [Fact]
    public async Task HashesThePasswordAsUtf8()
    {
        var service = Answering("");
        await TimesLeaked(service, "contraseña");
        Assert.Equal("8C31B", Assert.Single(service.Bodies));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task AnErrorFromTheServiceLetsThePasswordThrough(HttpStatusCode status) =>
        Assert.Equal(0, await TimesLeaked(Answering($"{Rest}:3\r\n", status)));

    [Fact]
    public async Task ANetworkFailureLetsThePasswordThrough() =>
        Assert.Equal(
            0,
            await TimesLeaked(new FakeService(_ => throw new HttpRequestException("no network")))
        );

    [Theory]
    [InlineData($"{Rest}:many\r\n")]
    [InlineData($"{Rest}\r\n")]
    [InlineData("<html></html>")]
    public async Task AGarbledAnswerLetsThePasswordThrough(string body) =>
        Assert.Equal(0, await TimesLeaked(Answering(body)));

    [Fact]
    public Task AServiceThatDoesNotAnswerInTimeLetsThePasswordThrough() =>
        AssertLetThroughAtTheLimit(
            new FakeService(async token =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return new HttpResponseMessage();
            })
        );

    [Fact]
    public Task ABodyThatDoesNotArriveInTimeLetsThePasswordThrough() =>
        AssertLetThroughAtTheLimit(
            new FakeService(_ =>
                Task.FromResult(
                    new HttpResponseMessage { Content = new StreamContent(new SilentStream()) }
                )
            )
        );
}
