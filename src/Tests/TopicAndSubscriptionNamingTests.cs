namespace NServiceBus.Transport.IBMMQ.Tests;

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
