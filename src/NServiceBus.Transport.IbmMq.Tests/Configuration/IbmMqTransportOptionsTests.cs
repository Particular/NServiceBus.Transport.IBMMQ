namespace NServiceBus.Transport.IbmMq.Tests.Configuration
{
    using System;
    using NUnit.Framework;

    [TestFixture]
    class IbmMqTransportOptionsTests
    {
        const int ExpectedMessageWaitInterval = 5000;

        [Test]
        public void Default_values_are_set_correctly()
        {
            var options = new IbmMqTransportOptions();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(options.QueueManagerName, Is.EqualTo(string.Empty));
                Assert.That(options.Host, Is.EqualTo("localhost"));
                Assert.That(options.Port, Is.EqualTo(1414));
                Assert.That(options.Channel, Is.EqualTo("DEV.ADMIN.SVRCONN"));
                Assert.That(options.Connections, Is.Empty);
                Assert.That(options.User, Is.Null);
                Assert.That(options.Password, Is.Null);
                Assert.That(options.ApplicationName, Is.Null);
                Assert.That(options.SslKeyRepository, Is.Null);
                Assert.That(options.CipherSpec, Is.Null);
                Assert.That(options.SslPeerName, Is.Null);
                Assert.That(options.KeyResetCount, Is.EqualTo(0));
                Assert.That(options.MessageWaitInterval, Is.EqualTo(TimeSpan.FromMilliseconds(ExpectedMessageWaitInterval)));
                Assert.That(options.MaxMessageLength, Is.EqualTo(4 * 1024 * 1024));
                Assert.That(options.CharacterSet, Is.EqualTo(1208));
                Assert.That(options.QueueNameFormatter, Is.Not.Null);
            }
        }

        [Test]
        public void Default_queue_name_formatter_returns_input_unchanged()
        {
            var options = new IbmMqTransportOptions();

            Assert.That(options.QueueNameFormatter!("MY.QUEUE"), Is.EqualTo("MY.QUEUE"));
        }

        [Test]
        public void Valid_default_options_pass_validation()
        {
            Assert.DoesNotThrow(() => new IbmMqTransport(_ => { }));
        }

        // Connection validation

        [Test]
        public void Validation_fails_when_host_is_null_and_no_connections()
        {
            Assert.That(() => new IbmMqTransport(o => o.Host = null),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("Host is required"));
        }

        [Test]
        public void Validation_fails_when_host_is_empty_and_no_connections()
        {
            Assert.That(() => new IbmMqTransport(o => o.Host = ""),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("Host is required"));
        }

        [Test]
        public void Validation_fails_when_host_is_whitespace_and_no_connections()
        {
            Assert.That(() => new IbmMqTransport(o => o.Host = "   "),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("Host is required"));
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(65536)]
        public void Validation_fails_for_invalid_port(int port)
        {
            Assert.That(() => new IbmMqTransport(o => o.Port = port),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("Port must be between 1 and 65535"));
        }

        [TestCase(1)]
        [TestCase(1414)]
        [TestCase(65535)]
        public void Validation_passes_for_valid_port(int port)
        {
            Assert.DoesNotThrow(() => new IbmMqTransport(o => o.Port = port));
        }

        [Test]
        public void Validation_fails_when_channel_is_null()
        {
            Assert.That(() => new IbmMqTransport(o => o.Channel = null),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("Channel is required"));
        }

        [Test]
        public void Validation_fails_when_channel_is_empty()
        {
            Assert.That(() => new IbmMqTransport(o => o.Channel = ""),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("Channel is required"));
        }

        // Connection name list validation

        [Test]
        public void Validation_passes_with_valid_connection_name_list()
        {
            Assert.DoesNotThrow(() => new IbmMqTransport(o =>
            {
                o.Connections.Add("mqhost1(1414)");
                o.Connections.Add("mqhost2(1415)");
            }));
        }

        [Test]
        public void Validation_fails_when_connection_entry_missing_parentheses()
        {
            Assert.That(() => new IbmMqTransport(o => o.Connections.Add("mqhost1:1414")),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("Connections format is invalid"));
        }

        [Test]
        public void Validation_fails_when_connection_entry_has_non_numeric_port()
        {
            Assert.That(() => new IbmMqTransport(o => o.Connections.Add("mqhost1(abc)")),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("Connections format is invalid"));
        }

        [Test]
        public void Validation_fails_when_connection_entry_has_invalid_port()
        {
            Assert.That(() => new IbmMqTransport(o => o.Connections.Add("mqhost1(0)")),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("Connections format is invalid"));
        }

        [Test]
        public void Validation_fails_when_connection_entry_has_empty_host()
        {
            Assert.That(() => new IbmMqTransport(o => o.Connections.Add("(1414)")),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("Connections format is invalid"));
        }

        [Test]
        public void Connection_name_list_takes_precedence_over_host_and_port()
        {
            Assert.DoesNotThrow(() => new IbmMqTransport(o =>
            {
                o.Host = null;
                o.Port = 0;
                o.Connections.Add("mqhost1(1414)");
            }));
        }

        // SSL validation

        [Test]
        public void Validation_fails_when_ssl_key_repository_set_without_cipher_spec()
        {
            Assert.That(() => new IbmMqTransport(o => o.SslKeyRepository = "*SYSTEM"),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("CipherSpec is required when SslKeyRepository is specified"));
        }

        [Test]
        public void Validation_fails_when_cipher_spec_set_without_ssl_key_repository()
        {
            Assert.That(() => new IbmMqTransport(o => o.CipherSpec = "TLS_RSA_WITH_AES_128_CBC_SHA256"),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("SslKeyRepository is required when CipherSpec is specified"));
        }

        [Test]
        public void Validation_passes_when_both_ssl_settings_specified()
        {
            Assert.DoesNotThrow(() => new IbmMqTransport(o =>
            {
                o.SslKeyRepository = "*SYSTEM";
                o.CipherSpec = "TLS_RSA_WITH_AES_128_CBC_SHA256";
            }));
        }

        [Test]
        public void Validation_fails_when_key_reset_count_is_negative()
        {
            Assert.That(() => new IbmMqTransport(o => o.KeyResetCount = -1),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("KeyResetCount must be 0 or greater"));
        }

        [TestCase(0)]
        [TestCase(40000)]
        public void Validation_passes_for_valid_key_reset_count(int count)
        {
            Assert.DoesNotThrow(() => new IbmMqTransport(o => o.KeyResetCount = count));
        }

        // Message processing validation

        [TestCase(99)]
        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(30001)]
        public void Validation_fails_for_invalid_message_wait_interval(int interval)
        {
            Assert.That(() => new IbmMqTransport(o => o.MessageWaitInterval = TimeSpan.FromMilliseconds(interval)),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("MessageWaitInterval must be between 100 and 30000"));
        }

        [TestCase(100)]
        [TestCase(5000)]
        [TestCase(30000)]
        public void Validation_passes_for_valid_message_wait_interval(int interval)
        {
            Assert.DoesNotThrow(() => new IbmMqTransport(o => o.MessageWaitInterval = TimeSpan.FromMilliseconds(interval)));
        }

        [TestCase(1023)]
        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(104857601)]
        public void Validation_fails_for_invalid_max_message_length(int length)
        {
            Assert.That(() => new IbmMqTransport(o => o.MaxMessageLength = length),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("MaxMessageLength must be between 1024"));
        }

        [TestCase(1024)]
        [TestCase(4 * 1024 * 1024)]
        [TestCase(104857600)]
        public void Validation_passes_for_valid_max_message_length(int length)
        {
            Assert.DoesNotThrow(() => new IbmMqTransport(o => o.MaxMessageLength = length));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void Validation_fails_for_invalid_character_set(int ccsid)
        {
            Assert.That(() => new IbmMqTransport(o => o.CharacterSet = ccsid),
                Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("CharacterSet must be a positive CCSID value"));
        }

        [TestCase(1208)]
        [TestCase(819)]
        [TestCase(1252)]
        public void Validation_passes_for_valid_character_set(int ccsid)
        {
            Assert.DoesNotThrow(() => new IbmMqTransport(o => o.CharacterSet = ccsid));
        }

        [Test]
        public void Constructor_throws_when_configure_action_is_null()
        {
            Assert.That(() => new IbmMqTransport((Action<IbmMqTransportOptions>)null!),
                Throws.TypeOf<ArgumentNullException>());
        }
    }
}
