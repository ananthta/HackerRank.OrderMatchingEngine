using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace OrderMatchingEngine
{
    class Program
    {
        static void Main(string[] args)
        {
            var operationHandler = DependencyServiceProvider.Get().GetService<IOperationHandler>();
            
            while (true)
            {
                var argument = Console.ReadLine();

                if (string.IsNullOrEmpty(argument))
                    break;

                if (argument == OperationType.Print)
                {
                    operationHandler.Print();
                }
                else
                {
                    var operation = OperationsFactory.Get(argument);
                    operationHandler.Process(operation);
                }              
            }
        }
    }

    internal interface IOperationHandler
    {
        void Print();
        void Process(Operation operation);
    }

    internal class OperationHandler : IOperationHandler
    {
        public OperationHandler(IBuySellOperationHandler buyOperationHandler, IBuySellOperationHandler sellOperationHandler)
        {
            _buyOperationHandler = buyOperationHandler;
            _sellOperationHandler = sellOperationHandler;
        }

        public void Print()
        {
            var result = new StringBuilder();
            result.Append("SELL:").Append(Environment.NewLine);

            foreach (var (key, value) in _sellOperationHandler.BuySellPriceToQuantityMap.OrderByDescending(e => e.Key))
            {
                if(value > 0)
                    result.Append($"{key} {value}").Append(Environment.NewLine);
            }
            
            result.Append("BUY:").Append(Environment.NewLine);
            
            foreach (var (key, value) in _buyOperationHandler.BuySellPriceToQuantityMap.OrderByDescending(e => e.Key))
            {
                if(value > 0)
                    result.Append($"{key} {value}").Append(Environment.NewLine);
            }

            Console.WriteLine(result.ToString());
        }

        public void Process(Operation operation)
        {
            // ReSharper disable once SwitchStatementMissingSomeCases
            switch (operation.OperationType)
            {
                case OperationType.Buy:
                {
                    var op = (BuySellOperation) operation;
                    var originalQuantity = op.Quantity;
                    _buyOperationHandler.Process(op);
                    var tradeResult = _sellOperationHandler.Trade(op);

                    if (tradeResult != null && tradeResult.Any())
                        _buyOperationHandler.BalancePriceToQuantityMap(op.Price, op.Quantity-originalQuantity);
                    
                    _buyOperationHandler.HandleStaleIocOperations();
                    break;
                }
                case OperationType.Sell:
                {
                    var op = (BuySellOperation) operation;
                    var originalQuantity = op.Quantity;
                    _sellOperationHandler.Process(op);
                    var tradeResult = _buyOperationHandler.Trade(op);
                    
                    if (tradeResult != null && tradeResult.Any())
                        _sellOperationHandler.BalancePriceToQuantityMap(op.Price, op.Quantity-originalQuantity);        
                    
                    _sellOperationHandler.HandleStaleIocOperations();
                    break;
                }
                case OperationType.Cancel:
                {
                    _buyOperationHandler.Cancel(operation.OrderId);
                    _sellOperationHandler.Cancel(operation.OrderId);
                    break;
                }
                case OperationType.Modify:
                {
                    var op = (ModifyOperation) operation;
                    Modify(op);
                    break;
                }
            }
        }

        private void Modify(ModifyOperation modifyOperation)
        {
            var buyOperation = _buyOperationHandler.Find(modifyOperation.OrderId);
            if (buyOperation != null)
            {
                _buyOperationHandler.Cancel(buyOperation.OrderId);
                ReInsertModifiedBuySellOperation(buyOperation, modifyOperation);
            }
            else
            {
                var sellOperation = _sellOperationHandler.Find(modifyOperation.OrderId);
                if (sellOperation == null) return;
                _sellOperationHandler.Cancel(sellOperation.OrderId); 
                ReInsertModifiedBuySellOperation(sellOperation, modifyOperation);
            }
        }

        private void ReInsertModifiedBuySellOperation(BuySellOperation previousBuySellOperation, ModifyOperation modifyOperation)
        {
            if (previousBuySellOperation.OrderType == OrderType.Ioc) return;
   
            var newOperation = new BuySellOperation(modifyOperation.OperationType, previousBuySellOperation.OrderType,
                modifyOperation.NewPrice, modifyOperation.NewQuantity, modifyOperation.OrderId);

            if (modifyOperation.BuyOrSell == OperationType.Sell)
            {
                _sellOperationHandler.Process(newOperation);
            }
            else
            {
                _buyOperationHandler.Process(newOperation);
            }
        }

        private readonly IBuySellOperationHandler _buyOperationHandler;
        private readonly IBuySellOperationHandler _sellOperationHandler;
    }


    internal interface IBuySellOperationHandler
    {
        void Cancel(string orderId);
        void HandleStaleIocOperations();
        BuySellOperation Find(string orderId);
        void Process(BuySellOperation buySellOperation);
        void BalancePriceToQuantityMap(int price, int quantity);
        IList<BuySellOperation> Trade(BuySellOperation operation);
        void BalancePriceToQuantityMap(BuySellOperation operation);
        
        Dictionary<int, int> BuySellPriceToQuantityMap { get; }
    }

    internal class BuySellOperationHandler : IBuySellOperationHandler
    {
        public BuySellOperationHandler()
        {
            BuySellOperations = new SortedSet<BuySellOperation>();
            BuySellPriceToQuantityMap = new Dictionary<int, int>();
        }

        public void Process(BuySellOperation buySellOperation)
        {           
            // Put the current buy / sell operation on the queue.
            BuySellOperations.Add(buySellOperation);
            
            // Add buyOperation to historical price to quantity map.
            if (BuySellPriceToQuantityMap.TryGetValue(buySellOperation.Price, out var quantity))
            {
                BuySellPriceToQuantityMap[buySellOperation.Price] = quantity + buySellOperation.Quantity;
            }
            else
            {
                BuySellPriceToQuantityMap.Add(buySellOperation.Price, buySellOperation.Quantity);
            }
        }

        public void Cancel(string orderId)
        {
            var operation = Find(orderId);

            if(operation == null) return;

            BalancePriceToQuantityMap(operation);

            BuySellOperations.Remove(operation);
        }

        public BuySellOperation Find(string orderId)
        {
            foreach (var operation in BuySellOperations)
            {
                if (operation.OrderId == orderId)
                    return operation;
            }

            return null;
        }

        public IList<BuySellOperation> Trade(BuySellOperation operation)
        {
            var result = new List<BuySellOperation>();
            var operationsToBeRemoved = new List<BuySellOperation>();
            
            for (var i = 0; i < BuySellOperations.Count; i++)
            {
                if (operation.Quantity == 0)
                    break;

                if ((operation.OperationType == OperationType.Buy &&
                     operation.Price >= BuySellOperations.ElementAt(i).Price) ||
                    (operation.OperationType == OperationType.Sell &&
                     operation.Price <= BuySellOperations.ElementAt(i).Price))
                {
                    AddToTradeResult(operation, result, BuySellOperations.ElementAt(i), operationsToBeRemoved);
                }
            }

            foreach (var operationToBeRemoved in operationsToBeRemoved)
            {
                Cancel(operationToBeRemoved.OrderId);
            }

            return result;
        }

        public void BalancePriceToQuantityMap(BuySellOperation operation)
        {
            if (BuySellPriceToQuantityMap.TryGetValue(operation.Price, out _))
            {
                BuySellPriceToQuantityMap[operation.Price] -= operation.Quantity;
            }
        }
        
        public void BalancePriceToQuantityMap(int price, int quantity)
        {
            if (BuySellPriceToQuantityMap.TryGetValue(price, out _))
            {
                BuySellPriceToQuantityMap[price] += quantity;
            }
        }

        private void AddToTradeResult(BuySellOperation currentOperation, ICollection<BuySellOperation> result, BuySellOperation toBeCompared, IList<BuySellOperation> toBeRemoved)
        {         
            // Selling quantity is less equal to buying quantity.
            if (toBeCompared.Quantity <= currentOperation.Quantity)
            {
                result.Add(toBeCompared);
                currentOperation.Quantity -= toBeCompared.Quantity;
                toBeRemoved.Add(toBeCompared);
            }                  
            // If Selling quantity is greater than buying quantity.
            else if (toBeCompared.Quantity > currentOperation.Quantity)
            {
                var originalQuantity = currentOperation.Quantity;
                result.Add(toBeCompared);
                currentOperation.Quantity = originalQuantity - toBeCompared.Quantity < 0
                    ? 0
                    : originalQuantity - toBeCompared.Quantity;
                toBeCompared.Quantity -= originalQuantity;
                BalancePriceToQuantityMap(toBeCompared);
            }
            PrintTrade(currentOperation, toBeCompared);
        }

        private static void PrintTrade(BuySellOperation input, BuySellOperation tradeResult)
        {
            var outPut = new StringBuilder();

            outPut.Append("TRADE ");
            if (tradeResult.TimeStamp > input.TimeStamp)
            {
                outPut.Append($"{input} {tradeResult.Quantity} {tradeResult} {tradeResult.Quantity}").Append(Environment.NewLine);
            }
            else
            {
                outPut.Append($"{tradeResult} {tradeResult.Quantity} {input} {tradeResult.Quantity}").Append(Environment.NewLine);
            }


            Console.Write(outPut.ToString());
        }

        public void HandleStaleIocOperations()
        {
            // This means previous Ioc is still pending and needs to be removed.
            if (BuySellOperations.ElementAt(0).OrderType == OrderType.Ioc)
                Cancel(BuySellOperations.ElementAt(0).OrderId);
        }
        
        private SortedSet<BuySellOperation> BuySellOperations { get; }
        public Dictionary<int, int> BuySellPriceToQuantityMap { get; }
    }

    public abstract class Operation
    {
        public string OperationType { get; }
        public string OrderId { get; }

        public DateTime TimeStamp { get; }

        protected Operation(string operationType, string orderId)
        {
            TimeStamp = DateTime.UtcNow;
            OperationType = operationType;
            OrderId = orderId;
        }

        public override int GetHashCode()
        {
            return 17 * 23 + TimeStamp.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;
            var other = (Operation) obj;

            return other.OrderId == OrderId && other.TimeStamp == TimeStamp;
        }
    }

    public class BuySellOperation : Operation, IComparable<BuySellOperation>
    {
        public string OrderType { get; }
        public int Price { get; }
        public int Quantity { get; set; }

        public BuySellOperation(string operationType, string orderType, int price, int quantity, string orderId) 
            : base(operationType, orderId)
        {
            Price = price;
            Quantity = quantity;
            OrderType = orderType;
        }

        public int CompareTo(BuySellOperation other)
        {
            return TimeStamp.CompareTo(other.TimeStamp);
        }

        public override string ToString()
        {
            var result = new StringBuilder();
            result.Append($"{OrderId} {Price} ");
            return result.ToString();
        }
    }

    public class CancelOperation : Operation
    {
        public CancelOperation(string operationType, string orderId)
            : base(operationType, orderId){}
    }

    public class ModifyOperation : Operation
    {
        public string BuyOrSell { get; }
        public int NewPrice { get; }
        public int NewQuantity { get; }

        public ModifyOperation(string operationType, string orderId, string buyOrSell, int newPrice, int newQuantity)
            : base(operationType, orderId)
        {
            NewPrice    = newPrice;
            BuyOrSell   = buyOrSell;
            NewQuantity = newQuantity;
        }
    }
    
    public static class OperationType
    {
        public const string Buy    = "BUY";
        public const string Sell   = "SELL";
        public const string Print  = "PRINT";
        public const string Cancel = "CANCEL";
        public const string Modify = "MODIFY";

        public static bool IsValid(string operationType)
        {
            if (string.IsNullOrEmpty(operationType))
                return false;

            switch (operationType)
            {
                case Buy:
                case Sell:
                case Print:
                case Cancel:
                case Modify:
                    return true;
                default:
                    return false;
            }
        }
    }

    public static class OrderType
    {
        public const string Ioc = "IOC";
        private const string Gfd = "GFD";

        public static bool IsValid(string orderType)
        {
            if (string.IsNullOrEmpty(orderType))
                return false;

            switch (orderType)
            {
                case Ioc:
                case Gfd:
                    return true;
                default:
                    return false;
            }
        }
    }
    
    public static class OperationsFactory
    {
        public static Operation Get(string argument)
        {
            if (string.IsNullOrEmpty(argument))
                return null;

            var arguments = argument.Split(' ');
                                    
            if (arguments == null || arguments.Length == 0 || !AreArgumentsValid(arguments))
                return null;
            
            switch (arguments.ElementAt(0))
            {
                case OperationType.Buy:
                case OperationType.Sell:
                    return new BuySellOperation(arguments.ElementAt(0), arguments.ElementAt(1), int.Parse(arguments.ElementAt(2)),
                        int.Parse(arguments.ElementAt(3)), arguments.ElementAt(4));
                case OperationType.Cancel:
                    return new CancelOperation(arguments.ElementAt(0), arguments.ElementAt(1));
                case OperationType.Modify:
                    return new ModifyOperation(arguments.ElementAt(0), arguments.ElementAt(1), arguments.ElementAt(2), int.Parse(arguments.ElementAt(3)),
                        int.Parse(arguments.ElementAt(4)));
                default: throw new ArgumentException("Unknown operation type");
            }
        }

        private static bool AreArgumentsValid(IReadOnlyCollection<string> arguments)
        {
            if (!OperationType.IsValid(arguments.ElementAt(0))) return false;
            return arguments.ElementAt(0) != OperationType.Buy || OrderType.IsValid(arguments.ElementAt(1));
        }
    }

    public static class DependencyServiceProvider
    {
        public static IServiceProvider Get()
        {
            return _serviceProvider ?? (_serviceProvider = GetRegisteredServices().BuildServiceProvider());
        }

        private static IServiceCollection GetRegisteredServices()
        {
            var serviceCollection = new ServiceCollection();

            serviceCollection.TryAddSingleton<IOperationHandler, OperationHandler>();
            serviceCollection.TryAddTransient<IBuySellOperationHandler, BuySellOperationHandler>();
            
            return serviceCollection;
        }

        private static IServiceProvider _serviceProvider;
    }
}