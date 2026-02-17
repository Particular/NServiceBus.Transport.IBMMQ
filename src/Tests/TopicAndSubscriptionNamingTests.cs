namespace NServiceBus.Transport.IbmMq.Tests;

using System;
using System.Linq;
using NUnit.Framework;

// Short name types for predictable FullName lengths
class Evt;
class VeryLongEventNameThatWillExceedTheFortyEightCharacterLimit;

class O
{
    internal class I;
}

interface IMyEvent : IEvent;
class BaseEvent : IMyEvent;
class ConcreteEvent : BaseEvent;
