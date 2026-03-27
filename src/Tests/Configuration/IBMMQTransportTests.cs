#pragma warning disable PS0024 // "I" in IBMMQ is from IBM, not an interface prefix
namespace NServiceBus.Transport.IBMMQ.Tests.Configuration
{
    using System;
    using NUnit.Framework;

    [TestFixture]
    class IBMMQTransportTests
    {
        const int ExpectedMessageWaitInterval = 5000;

        [Test]
        public void Default_values_are_set_correctly()
        {
            var transport = new IBMMQTransport();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(transport.QueueManagerName, Is.EqualTo(string.Empty));
                Assert.That(transport.Host, Is.EqualTo("localhost"));
                Assert.That(transport.Port, Is.EqualTo(1414));
                Assert.That(transport.Channel, Is.EqualTo("DEV.ADMIN.SVRCONN"));
                Assert.That(transport.Connections, Is.Empty);
                Assert.That(transport.User, Is.Null);
                Assert.That(transport.Password, Is.Null);
                Assert.That(transport.ApplicationName, Is.Null);
                Assert.That(transport.SslKeyRepository, Is.Null);
                Assert.That(transport.CipherSpec, Is.Null);
                Assert.That(transport.SslPeerName, Is.Null);
                Assert.That(transport.KeyResetCount, Is.EqualTo(0));
                Assert.That(transport.MessageWaitInterval, Is.EqualTo(TimeSpan.FromMilliseconds(ExpectedMessageWaitInterval)));
                Assert.That(transport.CharacterSet, Is.EqualTo(1208));
                Assert.That(transport.ResourceNameSanitizer, Is.Not.Null);
            }
        }

        [Test]
        public void Default_queue_name_formatter_returns_input_unchanged()
        {
            var transport = new IBMMQTransport();

            Assert.That(transport.ResourceNameSanitizer!("MY.QUEUE"), Is.EqualTo("MY.QUEUE"));
        }

        [Test]
        public void Valid_default_transport_passes_validation()
        {
            Assert.DoesNotThrow(() => IBMMQTransportValidator.Validate(new IBMMQTransport()));
        }

        // Connection validation

        [Test]
        public void Validation_fails_when_host_is_null_and_no_connections()
        {
            Assert.That(() => IBMMQTransportValidator.Validate(new IBMMQTransport { Host = null }),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("Host is required"));
        }

        [Test]
        public void Validation_fails_when_host_is_empty_and_no_connections()
        {
            Assert.That(() => IBMMQTransportValidator.Validate(new IBMMQTransport { Host = "" }),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("Host is required"));
        }

        [Test]
        public void Validation_fails_when_host_is_whitespace_and_no_connections()
        {
            Assert.That(() => IBMMQTransportValidator.Validate(new IBMMQTransport { Host = "   " }),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("Host is required"));
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(65536)]
        public void Port_rejects_invalid_value(int port)
        {
            Assert.That(() => new IBMMQTransport { Port = port },
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCase(1)]
        [TestCase(1414)]
        [TestCase(65535)]
        public void Port_accepts_valid_value(int port)
        {
            Assert.DoesNotThrow(() => new IBMMQTransport { Port = port });
        }

        [Test]
        public void Validation_fails_when_channel_is_null()
        {
            Assert.That(() => IBMMQTransportValidator.Validate(new IBMMQTransport { Channel = null }),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("Channel is required"));
        }

        [Test]
        public void Validation_fails_when_channel_is_empty()
        {
            Assert.That(() => IBMMQTransportValidator.Validate(new IBMMQTransport { Channel = "" }),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("Channel is required"));
        }

        // Connection name list validation

        [Test]
        public void Validation_passes_with_valid_connection_name_list()
        {
            var transport = new IBMMQTransport();
            transport.Connections.Add("mqhost1(1414)");
            transport.Connections.Add("mqhost2(1415)");

            Assert.DoesNotThrow(() => IBMMQTransportValidator.Validate(transport));
        }

        [Test]
        public void Validation_fails_when_connection_entry_missing_parentheses()
        {
            var transport = new IBMMQTransport();
            transport.Connections.Add("mqhost1:1414");

            Assert.That(() => IBMMQTransportValidator.Validate(transport),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("Connections format is invalid"));
        }

        [Test]
        public void Validation_fails_when_connection_entry_has_non_numeric_port()
        {
            var transport = new IBMMQTransport();
            transport.Connections.Add("mqhost1(abc)");

            Assert.That(() => IBMMQTransportValidator.Validate(transport),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("Connections format is invalid"));
        }

        [Test]
        public void Validation_fails_when_connection_entry_has_invalid_port()
        {
            var transport = new IBMMQTransport();
            transport.Connections.Add("mqhost1(0)");

            Assert.That(() => IBMMQTransportValidator.Validate(transport),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("Connections format is invalid"));
        }

        [Test]
        public void Validation_fails_when_connection_entry_has_empty_host()
        {
            var transport = new IBMMQTransport();
            transport.Connections.Add("(1414)");

            Assert.That(() => IBMMQTransportValidator.Validate(transport),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("Connections format is invalid"));
        }

        [Test]
        public void Connection_name_list_takes_precedence_over_host_and_port()
        {
            var transport = new IBMMQTransport
            {
                Host = null
            };
            transport.Connections.Add("mqhost1(1414)");

            Assert.DoesNotThrow(() => IBMMQTransportValidator.Validate(transport));
        }

        // SSL validation

        [Test]
        public void Validation_fails_when_ssl_key_repository_set_without_cipher_spec()
        {
            Assert.That(() => IBMMQTransportValidator.Validate(new IBMMQTransport { SslKeyRepository = "*SYSTEM" }),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("CipherSpec is required when SslKeyRepository is specified"));
        }

        [Test]
        public void Validation_fails_when_cipher_spec_set_without_ssl_key_repository()
        {
            Assert.That(() => IBMMQTransportValidator.Validate(new IBMMQTransport { CipherSpec = "TLS_RSA_WITH_AES_128_CBC_SHA256" }),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("SslKeyRepository is required when CipherSpec is specified"));
        }

        [Test]
        public void Validation_passes_when_both_ssl_settings_specified()
        {
            Assert.DoesNotThrow(() => IBMMQTransportValidator.Validate(new IBMMQTransport
            {
                SslKeyRepository = "*SYSTEM",
                CipherSpec = "TLS_RSA_WITH_AES_128_CBC_SHA256"
            }));
        }

        [Test]
        public void KeyResetCount_rejects_negative_value()
        {
            Assert.That(() => new IBMMQTransport { KeyResetCount = -1 },
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCase(0)]
        [TestCase(40000)]
        public void KeyResetCount_accepts_valid_value(int count)
        {
            Assert.DoesNotThrow(() => new IBMMQTransport { KeyResetCount = count });
        }

        // Message processing validation

        [TestCase(99)]
        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(30001)]
        public void MessageWaitInterval_rejects_invalid_value(int interval)
        {
            Assert.That(() => new IBMMQTransport { MessageWaitInterval = TimeSpan.FromMilliseconds(interval) },
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCase(100)]
        [TestCase(5000)]
        [TestCase(30000)]
        public void MessageWaitInterval_accepts_valid_value(int interval)
        {
            Assert.DoesNotThrow(() => new IBMMQTransport { MessageWaitInterval = TimeSpan.FromMilliseconds(interval) });
        }

        [Test]
        public void TopicNaming_rejects_null()
        {
            Assert.That(() => new IBMMQTransport { TopicNaming = null! },
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ResourceNameSanitizer_rejects_null()
        {
            Assert.That(() => new IBMMQTransport { ResourceNameSanitizer = null! },
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void TimeToWaitBeforeTriggeringCircuitBreaker_rejects_zero()
        {
            Assert.That(() => new IBMMQTransport { TimeToWaitBeforeTriggeringCircuitBreaker = TimeSpan.Zero },
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void TimeToWaitBeforeTriggeringCircuitBreaker_rejects_negative()
        {
            Assert.That(() => new IBMMQTransport { TimeToWaitBeforeTriggeringCircuitBreaker = TimeSpan.FromSeconds(-1) },
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void CharacterSet_rejects_invalid_value(int ccsid)
        {
            Assert.That(() => new IBMMQTransport { CharacterSet = ccsid },
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCase(1208)]
        [TestCase(819)]
        [TestCase(1252)]
        public void CharacterSet_accepts_valid_value(int ccsid)
        {
            Assert.DoesNotThrow(() => new IBMMQTransport { CharacterSet = ccsid });
        }
    }
}
