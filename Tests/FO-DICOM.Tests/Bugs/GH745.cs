﻿// Copyright (c) 2012-2019 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using FellowOakDicom.Log;
using FellowOakDicom.Network;
using FellowOakDicom.Tests.Helpers;
using FellowOakDicom.Tests.Network;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FellowOakDicom.Network.Client;
using Xunit;
using Xunit.Abstractions;

namespace FellowOakDicom.Tests.Bugs
{

    [Collection("Network")]
    public class GH745: IClassFixture<GlobalFixture>
    {
        private readonly XUnitDicomLogger _logger;
        private readonly IDicomServerFactory _serverFactory;
        private readonly IDicomClientFactory _clientFactory;

        public GH745(ITestOutputHelper output, GlobalFixture globalFixture)
        {
            _logger = new XUnitDicomLogger(output)
                .IncludeTimestamps()
                .IncludeThreadId()
                .WithMinimumLevel(LogLevel.Warning);
            _serverFactory = globalFixture.GetRequiredService<IDicomServerFactory>();
            _clientFactory = globalFixture.GetRequiredService<IDicomClientFactory>();
        }

        [Theory]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        public async Task DicomClientShallNotCloseConnectionTooEarly_CEchoSerialAsync(int expected)
        {
            var port = Ports.GetNext();
            var testLogger = _logger.IncludePrefix("GH745");
            var clientLogger = _logger.IncludePrefix(nameof(DicomClient));
            var serverLogger = _logger.IncludePrefix(nameof(DicomCEchoProvider));

            using (var server = _serverFactory.Create<DicomCEchoProvider>(port))
            {
                server.Logger = serverLogger;
                while (!server.IsListening) await Task.Delay(50);

                var actual = 0;

                var client = _clientFactory.Create("127.0.0.1", port, false, "SCU", "ANY-SCP");
                client.Logger = clientLogger;
                client.ClientOptions.AssociationRequestTimeoutInMs = 600 * 1000;
                client.ClientOptions.AssociationLingerTimeoutInMs = 1; // No need to linger, we only send one request at a time

                for (var i = 0; i < expected; i++)
                {
                    await client.AddRequestAsync(
                        new DicomCEchoRequest
                        {
                            OnResponseReceived = (req, res) =>
                            {
                                testLogger.Info("Response #{0} / expected #{1}", actual, req.UserState);
                                Interlocked.Increment(ref actual);
                                testLogger.Info("         #{0} / expected #{1}", actual - 1, req.UserState);
                            },
                            UserState = i
                        }
                    ).ConfigureAwait(false);
                    testLogger.Info("Sending #{0}", i);
                    await client.SendAsync().ConfigureAwait(false);
                    testLogger.Info("Sent (or timed out) #{0}", i);
                    //if (i != actual-1)
                    //{
                    //    output.WriteLine("  waiting #{0}", i);
                    //    await Task.Delay((int)TimeSpan.FromSeconds(1).TotalMilliseconds);
                    //    output.WriteLine("  waited #{0}", i);
                    //}
                }

                Assert.Equal(expected, actual);
            }
        }

        [Theory]
        [InlineData(10)]
        [InlineData(75)]
        [InlineData(200)]
        [InlineData(1)]
        public async Task DicomClientShallNotCloseConnectionTooEarly_CEchoParallelAsync(int expected)
        {
            int port = Ports.GetNext();

            var testLogger = _logger.IncludePrefix("GH745");
            var clientLogger = _logger.IncludePrefix(nameof(DicomClient));
            var serverLogger = _logger.IncludePrefix(nameof(DicomCEchoProvider));

            using (var server = _serverFactory.Create<DicomCEchoProvider>(port))
            {
                server.Logger = serverLogger;
                while (!server.IsListening) await Task.Delay(50);

                var actual = 0;

                var requests = Enumerable.Range(0, expected).Select(
                    async requestIndex =>
                    {
                        var client = _clientFactory.Create("127.0.0.1", port, false, "SCU", "ANY-SCP");
                        client.ClientOptions.AssociationRequestTimeoutInMs = 600 * 1000;
                        client.Logger = clientLogger;

                        await client.AddRequestAsync(
                            new DicomCEchoRequest
                            {
                                OnResponseReceived = (req, res) =>
                                {
                                    testLogger.Info("Response #{0}", requestIndex);
                                    Interlocked.Increment(ref actual);
                                }
                            }
                        ).ConfigureAwait(false);

                        testLogger.Info("Sending #{0}", requestIndex);
                        await client.SendAsync().ConfigureAwait(false);
                        testLogger.Info("Sent (or timed out) #{0}", requestIndex);
                    }
                ).ToArray();

                await Task.WhenAll(requests).ConfigureAwait(false);

                Assert.Equal(expected, actual);
            }
        }

    }
}
