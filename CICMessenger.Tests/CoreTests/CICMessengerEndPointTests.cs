using System;
using System.Net;
using FluentAssertions;
using CICMessenger.Core;
using Xunit;

namespace CICMessenger.Tests.CoreTests
{
    public class CICMessengerEndPointTests
    {
        private static IPEndPoint MakeEndPoint(string ip = "192.168.1.1", int port = 9000)
            => new IPEndPoint(IPAddress.Parse(ip), port);

        [Fact]
        public void Constructor_SetsClientIdAndAddress()
        {
            var address = MakeEndPoint();
            var ep = new CICMessengerEndPoint("client1", address);

            ep.ClientID.Should().Be("client1");
            ep.Address.Should().Be(address);
        }

        [Fact]
        public void Constructor_ThrowsOnNullOrEmptyId()
        {
            var address = MakeEndPoint();

            Action act = () => new CICMessengerEndPoint("", address);

            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void CopyConstructor_CopiesValues()
        {
            var original = new CICMessengerEndPoint("client2", MakeEndPoint("10.0.0.1", 8080));
            var copy = new CICMessengerEndPoint(original);

            copy.ClientID.Should().Be(original.ClientID);
            copy.Address.Should().Be(original.Address);
        }

        [Fact]
        public void Equals_ReturnsTrueForSameEndpoint()
        {
            var a = new CICMessengerEndPoint("c1", MakeEndPoint());
            var b = new CICMessengerEndPoint("c1", MakeEndPoint());

            a.Equals(b).Should().BeTrue();
        }

        [Fact]
        public void Equals_ReturnsFalseForDifferentClientId()
        {
            var a = new CICMessengerEndPoint("c1", MakeEndPoint());
            var b = new CICMessengerEndPoint("c2", MakeEndPoint());

            a.Equals(b).Should().BeFalse();
        }

        [Fact]
        public void Equals_ReturnsFalseForDifferentAddress()
        {
            var a = new CICMessengerEndPoint("c1", MakeEndPoint("10.0.0.1", 9000));
            var b = new CICMessengerEndPoint("c1", MakeEndPoint("10.0.0.2", 9000));

            a.Equals(b).Should().BeFalse();
        }

        [Fact]
        public void Equals_ReturnsFalseForNull()
        {
            var ep = new CICMessengerEndPoint("c1", MakeEndPoint());

            ep.Equals(null).Should().BeFalse();
        }

        [Fact]
        public void GetHashCode_SameForEqualEndpoints()
        {
            var a = new CICMessengerEndPoint("c1", MakeEndPoint());
            var b = new CICMessengerEndPoint("c1", MakeEndPoint());

            a.GetHashCode().Should().Be(b.GetHashCode());
        }

        [Fact]
        public void GetHashCode_DiffersForDifferentEndpoints()
        {
            var a = new CICMessengerEndPoint("c1", MakeEndPoint());
            var b = new CICMessengerEndPoint("c2", MakeEndPoint("10.0.0.2", 8080));

            a.GetHashCode().Should().NotBe(b.GetHashCode());
        }

        [Fact]
        public void ToString_ContainsClientIdAndAddress()
        {
            var address = MakeEndPoint("192.168.1.100", 5555);
            var ep = new CICMessengerEndPoint("myClient", address);

            string result = ep.ToString();

            result.Should().Contain("myClient");
            result.Should().Contain("@");
            result.Should().Be("myClient@192.168.1.100:5555");
        }
    }
}
