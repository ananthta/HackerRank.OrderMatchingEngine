using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace OrderMatchingEngine
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
        }
    }

    internal class OperationHandler
    {
        public OperationHandler(IBuySellOperationHandler buyOperationHandler, IBuySellOperationHandler sellOperationHandler)
        {
            _buyOperationHandler = buyOperationHandler;
            _sellOperationHandler = sellOperationHandler;
        }

        public string Print()
        {
            var result = new StringBuilder();
            result.Append("SELL:").Append(Environment.NewLine);

            foreach (var (key, value) in _sellOperationHandler.HistoricalBuySellPriceToQuantityMap)
            {
                result.Append($"{key} {value}").Append(Environment.NewLine);
            }
            
            result.Append("BUY:").Append(Environment.NewLine);
            
            foreach (var (key, value) in _buyOperationHandler.HistoricalBuySellPriceToQuantityMap)
            {
                result.Append($"{key} {value}").Append(Environment.NewLine);
            }

            return result.ToString();
        }

        public void Process(Operation operation)
        {
 
        }




        private static void PrintTrade(BuySellOperation buyOperation, BuySellOperation sellOperation)
        {
            var result = new StringBuilder();
            result.Append("TRADE ");

            result.Append(buyOperation.TimeStamp > sellOperation.TimeStamp
                ? $"{buyOperation} {sellOperation}"
                : $"{sellOperation} {buyOperation}");

            Console.WriteLine(result.ToString());
        }

        private readonly IBuySellOperationHandler _buyOperationHandler;
        private readonly IBuySellOperationHandler _sellOperationHandler;
    }


    internal interface IBuySellOperationHandler
    {
        void Process(BuySellOperation buySellOperation);
        SortedSet<BuySellOperation> BuySellOperations { get; }
        Dictionary<int, int> HistoricalBuySellPriceToQuantityMap { get; }
    }

    internal class BuySellOperationHandler : IBuySellOperationHandler
    {
        public BuySellOperationHandler()
        {
            BuySellOperations = new SortedSet<BuySellOperation>();
            HistoricalBuySellPriceToQuantityMap = new Dictionary<int, int>();
        }

        public void Process(BuySellOperation buySellOperation)
        {
            if (buySellOperation.OperationType != OperationType.Sell)
                return;
            
            // Put the current buy / sell operation on the queue.
            BuySellOperations.Add(buySellOperation);
            
            // Add buyOperation to historical price to quantity map.
            if (HistoricalBuySellPriceToQuantityMap.TryGetValue(buySellOperation.Price, out var quantity))
            {
                HistoricalBuySellPriceToQuantityMap[buySellOperation.Price] = quantity + buySellOperation.Quantity;
            }
            else
            {
                HistoricalBuySellPriceToQuantityMap.Add(buySellOperation.Price, buySellOperation.Quantity);
            }
        }

        public IList<BuySellOperation> Trade(BuySellOperation operation)
        {            
            var result = new List<BuySellOperation>();
            var quantity = operation.Quantity;

            for (var i = 0; i < BuySellOperations.Count; i++)
            {
                if (quantity == 0)
                    break;

                // Input is buy operation and we are searching for sell operations.
                if (operation.OperationType == OperationType.Buy &&
                    operation.Price >= BuySellOperations.ElementAt(i).Price)
                {
                    AddToTradeResult(ref quantity, result, operation, BuySellOperations.ElementAt(i));
                }
                // Input is sell operation and we are searching for buy operations.
                else if(operation.OperationType == OperationType.Sell &&
                        operation.Price < BuySellOperations.ElementAt(i).Price)
                {
                    AddToTradeResult(ref quantity, result, operation, BuySellOperations.ElementAt(i));
                }
            }

            return result;
        }

        private void AddToTradeResult(ref int remainingQuantity, IList<BuySellOperation> result, BuySellOperation input, BuySellOperation toBeCompared)
        {
            // Selling quantity is less equal to buying quantity.
            if (toBeCompared.Quantity <= remainingQuantity)
            {
                result.Add(toBeCompared);
                remainingQuantity -= toBeCompared.Quantity;
                BuySellOperations.Remove(toBeCompared);
            }
                    
            // If Selling quantity is greater than buying quantity.
            else if (toBeCompared.Quantity > remainingQuantity)
            {
                result.Add(toBeCompared);
                remainingQuantity -= toBeCompared.Quantity;
                toBeCompared.Quantity -= remainingQuantity;
            }
        }

        public SortedSet<BuySellOperation> BuySellOperations { get; }
        public Dictionary<int, int> HistoricalBuySellPriceToQuantityMap { get; }
    }

    public abstract class Operation
    {
        public string OperationType { get; }
        public string OrderId { get; }

        public DateTime TimeStamp { get; }

        public Operation(string operationType, string orderId)
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

    public class BuySellOperation : Operation
    {
        public string OrderType { get; set; }
        public int Price { get; set; }
        public int Quantity { get; set; }

        public BuySellOperation(string operationType, string orderType, int price, int quantity, string orderId) 
            : base(operationType, orderId)
        {
            Price = price;
            Quantity = quantity;
            OrderType = orderType;
        }

        public override string ToString()
        {
            var result = new StringBuilder();
            result.Append($"{OrderId} {Price} {Quantity}");
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
        public const string Gfd = "GFD";

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
            
            if (!AreArgumentsValid(arguments))
                return null;
            

            switch (arguments[0])
            {
                case OperationType.Buy:
                case OperationType.Sell:
                    return new BuySellOperation(arguments[0], arguments[1], int.Parse(arguments[2]),
                        int.Parse(arguments[2]), arguments[3]);
                    break;
                case OperationType.Cancel:
                    return new CancelOperation(arguments[0], arguments[1]);
                    break;
                case OperationType.Modify:
                    return new ModifyOperation(arguments[0], arguments[1], arguments[2], int.Parse(arguments[3]),
                        int.Parse(arguments[4]));
                    break;
                default: throw new ArgumentException("Unknown operation type");
            }
        }

        private static bool AreArgumentsValid(IList<string> arguments)
        {
            if (!OperationType.IsValid(arguments[0])) return false;
            return arguments[0] != OperationType.Buy || OrderType.IsValid(arguments[1]);
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

            serviceCollection.TryAddSingleton<IBuySellOperationHandler, BuySellOperationHandler>();
            
            return serviceCollection;
        }

        private static IServiceProvider _serviceProvider;
    }
}