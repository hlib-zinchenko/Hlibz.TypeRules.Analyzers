// Violations of the 'handlers' rule set: internal, sealed, with an IFooHandler companion
// interface, in a Handlers namespace.
namespace Sample.Violations
{
    // TR101: public. TR102: not sealed. TR104: no IListOrdersHandler. TR103: wrong namespace.
    public class ListOrdersHandler : IRequestHandler<int, string>
    {
        public string Handle(int request)
        {
            return $"orders/{request}";
        }
    }
}

namespace Sample.Violations.Handlers
{
    // TR104: the companion interface exists, but the handler doesn't implement it
    // (the code fix adds it to the base list).
    internal interface ICancelOrderHandler : IRequestHandler<int, bool>;

    internal sealed class CancelOrderHandler : IRequestHandler<int, bool>
    {
        public bool Handle(int request)
        {
            return true;
        }
    }

    // TR104: the handler implements an IShipOrderHandler, but that one doesn't extend
    // IRequestHandler, so it's no use for resolving the handler (no code fix).
    internal interface IShipOrderHandler;

    internal sealed class ShipOrderHandler : IShipOrderHandler, IRequestHandler<int, bool>
    {
        public bool Handle(int request)
        {
            return true;
        }
    }
}
