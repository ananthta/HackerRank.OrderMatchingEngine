using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
//using Microsoft.Extensions.DependencyInjection;
//using Microsoft.Extensions.DependencyInjection.Extensions;

class Solution {
    static void Main(String[] args) {
        // With dependency injection.
        //var operationHandler = DependencyServiceProvider.Get().GetService<IOperationHandler>();
        
        var operationHandler = new OperationHandler(new BuySellOperationHandler());
            
        while (true)
        {
            var argument = Console.ReadLine();
            if (string.IsNullOrEmpty(argument))
                break;
            if (argument.ToUpper() == OperationType.Print)
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
    public OperationHandler(IBuySellOperationHandler buySellOperationHandler)
    {
        _buySellOperationHandler = buySellOperationHandler;
    }
    public void Print()
    {
        var result = new StringBuilder();
        result.Append("SELL:").Append(Environment.NewLine);
        foreach (var entry in _buySellOperationHandler.SellPriceToQuantityMap.OrderByDescending(e => e.Key))
        {
            if(entry.Key >0 && entry.Value > 0)
                result.Append($"{entry.Key} {entry.Value}").Append(Environment.NewLine);
        }
        
        result.Append("BUY:").Append(Environment.NewLine);
        
        foreach (var entry in _buySellOperationHandler.BuyPriceToQuantityMap.OrderByDescending(e => e.Key))
        {
            if(entry.Key >0 && entry.Value > 0)
                result.Append($"{entry.Key} {entry.Value}").Append(Environment.NewLine);
        }
        Console.Write(result.ToString());
    }
    public void Process(Operation operation)
    {
        if (operation == null)
            return;
        
        // ReSharper disable once SwitchStatementMissingSomeCases
        switch (operation.OperationType)
        {
            case OperationType.Buy:
            case OperationType.Sell:
            {
                var op = (BuySellOperation) operation;
                _buySellOperationHandler.Process(op);
                _buySellOperationHandler.Trade(op);                       
                
                _buySellOperationHandler.HandleStaleBuyIocOperations();
                _buySellOperationHandler.HandleStaleSellIocOperations();
                
                break;
            }
            case OperationType.Cancel:
            {
                _buySellOperationHandler.Cancel(operation.OrderId);
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
        var buySellOperation = _buySellOperationHandler.Find(modifyOperation.OrderId);
        if(buySellOperation == null)
            return;

        if (buySellOperation.OperationType == OperationType.Buy)
        {
            _buySellOperationHandler.CancelBuyOperation(buySellOperation);
        }
        else
        {
            _buySellOperationHandler.CancelSellOperation(buySellOperation);
        }
        ReInsertModifiedBuySellOperation(buySellOperation, modifyOperation);
    }
    private void ReInsertModifiedBuySellOperation(BuySellOperation previousBuySellOperation, ModifyOperation modifyOperation)
    {
        if (previousBuySellOperation.OrderType == OrderType.Ioc) return;
  
        var newOperation = new BuySellOperation(modifyOperation.BuyOrSell, previousBuySellOperation.OrderType,
            modifyOperation.NewPrice, modifyOperation.NewQuantity, modifyOperation.OrderId);
        
        if (modifyOperation.BuyOrSell == OperationType.Sell)
        {
            Process(newOperation);
        }
        else
        {
            Process(newOperation);
        }
    }
    private readonly IBuySellOperationHandler _buySellOperationHandler;
}
internal interface IBuySellOperationHandler
{
    void Cancel(string orderId);
    void HandleStaleBuyIocOperations();
    void HandleStaleSellIocOperations();
    BuySellOperation Find(string orderId);
    void Trade(BuySellOperation operation);
    void Process(BuySellOperation buySellOperation);
    void CancelBuyOperation(BuySellOperation operation);
    void CancelSellOperation(BuySellOperation operation);

    
    Dictionary<long, long> BuyPriceToQuantityMap { get; }
    Dictionary<long, long> SellPriceToQuantityMap { get; }
}
internal class BuySellOperationHandler : IBuySellOperationHandler
{
    public BuySellOperationHandler()
    {
        BuyOperations          = new SortedSet<BuySellOperation>();
        SellOperations         = new SortedSet<BuySellOperation>();
        BuyPriceToQuantityMap  = new Dictionary<long, long>();
        SellPriceToQuantityMap = new Dictionary<long, long>();
    }
    
    public void HandleStaleBuyIocOperations()
    {
        // This means previous Ioc is still pending and needs to be removed.
        if (BuyOperations.Count > 0 && BuyOperations.ElementAt(0).OrderType == OrderType.Ioc)
            CancelBuyOperation(BuyOperations.ElementAt(0));
    }
    
    public void HandleStaleSellIocOperations()
    {
        // This means previous Ioc is still pending and needs to be removed.
        if (SellOperations.Count > 0 && SellOperations.ElementAt(0).OrderType == OrderType.Ioc)
            CancelSellOperation(SellOperations.ElementAt(0));
    }

    
    public void Process(BuySellOperation buySellOperation)
    {
        if (buySellOperation.Price <= 0 || buySellOperation.Quantity < 0 )
            return;
        
        if (buySellOperation.OperationType == OperationType.Buy)
        {
            // Put the current buy / sell operation on the queue.
            BuyOperations.Add(buySellOperation);
        
            // Add buyOperation to historical price to quantity map.
            if (buySellOperation.OrderType != OrderType.Ioc && BuyPriceToQuantityMap.TryGetValue(buySellOperation.Price, out var quantity))
            {
                BuyPriceToQuantityMap[buySellOperation.Price] = quantity + buySellOperation.Quantity;
            }
            else if(buySellOperation.OrderType != OrderType.Ioc)
            {
                BuyPriceToQuantityMap.Add(buySellOperation.Price, buySellOperation.Quantity);
            }
        }
        else
        {
            // Put the current buy / sell operation on the queue.
            SellOperations.Add(buySellOperation);
        
            // Add buyOperation to historical price to quantity map.
            if (buySellOperation.OrderType != OrderType.Ioc && SellPriceToQuantityMap.TryGetValue(buySellOperation.Price, out var quantity))
            {
                SellPriceToQuantityMap[buySellOperation.Price] = quantity + buySellOperation.Quantity;
            }
            else if(buySellOperation.OrderType != OrderType.Ioc)
            {
                SellPriceToQuantityMap.Add(buySellOperation.Price, buySellOperation.Quantity);
            }
        }
    }

    public void Cancel(string orderId)
    {
        var operation = Find(orderId);
        if(operation == null) return;
        BalancePriceToQuantityMap(operation);

        if (operation.OperationType == OperationType.Buy)
        {
            BuyOperations.Remove(operation);
        }
        else
        {
            SellOperations.Remove(operation);
        }
    }
    
    public BuySellOperation Find(string orderId)
    {
        foreach (var operation in SellOperations)
        {
            if (operation.OrderId == orderId)
                return operation;
        }

        foreach (var operation in BuyOperations)
        {
            if (operation.OrderId == orderId)
                return operation;
        }
        return null;
    }
    public void Trade(BuySellOperation operation)
    {
        var buysToBeRemoved = new List<BuySellOperation>();
        var sellsToBeRemoved = new List<BuySellOperation>();
        
        if (operation.OperationType == OperationType.Buy)
        {
            for (var i = 0; i < SellOperations.Count; i++)
            {
                if (operation.Quantity <= 0)
                    break;
                if (operation.Price >= SellOperations.ElementAt(i).Price)
                {
                    AddToTradeResult(operation, SellOperations.ElementAt(i), buysToBeRemoved, sellsToBeRemoved);
                }
            }
        }
        else
        {           
            for (var i = 0; i < BuyOperations.Count; i++)
            {
                if (operation.Quantity <= 0)
                    break;
                if (operation.Price <= BuyOperations.ElementAt(i).Price)
                {
                    AddToTradeResult(BuyOperations.ElementAt(i), operation, buysToBeRemoved, sellsToBeRemoved);
                }
            }
        }
        foreach (var sellToBeRemoved in sellsToBeRemoved)
        {
            CancelSellOperation(sellToBeRemoved);
        }
        foreach (var buyToBeRemoved in buysToBeRemoved)
        {
            CancelBuyOperation(buyToBeRemoved);
        }
    }


    private void AddToTradeResult(BuySellOperation buyOperation, BuySellOperation sellOperation,
        ICollection<BuySellOperation> buysToBeRemoved, ICollection<BuySellOperation> sellsToBeRemoved)
    {
        if (sellOperation.Quantity == buyOperation.Quantity)
        {
            PrintTrade(buyOperation, sellOperation, sellOperation.Quantity);
            sellsToBeRemoved.Add(sellOperation);
            buysToBeRemoved.Add(buyOperation);
        }
        else if (sellOperation.Quantity < buyOperation.Quantity)
        {
            PrintTrade(buyOperation, sellOperation, sellOperation.Quantity);
            if (BuyPriceToQuantityMap.TryGetValue(buyOperation.Price, out _))
            {
                BuyPriceToQuantityMap[buyOperation.Price] -= sellOperation.Quantity;
            }
            buyOperation.Quantity -= sellOperation.Quantity;
            sellsToBeRemoved.Add(sellOperation);
        }
        else if (sellOperation.Quantity > buyOperation.Quantity)
        {
            PrintTrade(buyOperation, sellOperation, buyOperation.Quantity);
            if (SellPriceToQuantityMap.TryGetValue(sellOperation.Price, out _))
            {
                SellPriceToQuantityMap[sellOperation.Price] -= buyOperation.Quantity;
            }
            sellOperation.Quantity -= buyOperation.Quantity;
            buysToBeRemoved.Add(buyOperation);
        }
    }

    private static void PrintTrade(BuySellOperation input, BuySellOperation tradeResult, long quantity)
    {
        var outPut = new StringBuilder();
        outPut.Append("TRADE ");
        if (tradeResult.TimeStamp > input.TimeStamp)
        {
            outPut.Append($"{input} {quantity} {tradeResult} {quantity}").Append(Environment.NewLine);
        }
        else
        {
            outPut.Append($"{tradeResult} {quantity} {input} {quantity}").Append(Environment.NewLine);
        }
        Console.Write(outPut.ToString());
    }
    
    public void CancelBuyOperation(BuySellOperation operation)
    {
        BalancePriceToQuantityMap(operation);
        BuyOperations.Remove(operation);
    }

    public void CancelSellOperation(BuySellOperation operation)
    {
        BalancePriceToQuantityMap(operation);
        SellOperations.Remove(operation);
    }
    
    private void BalancePriceToQuantityMap(BuySellOperation operation)
    {
        if (operation.OperationType == OperationType.Buy && BuyPriceToQuantityMap.TryGetValue(operation.Price, out _))
            BuyPriceToQuantityMap[operation.Price] -= operation.Quantity;
        else if (operation.OperationType == OperationType.Sell &&
                 SellPriceToQuantityMap.TryGetValue(operation.Price, out _))
            SellPriceToQuantityMap[operation.Price] -= operation.Quantity;
    }
   
    private SortedSet<BuySellOperation> BuyOperations { get; }
    private SortedSet<BuySellOperation> SellOperations { get; }
    
    public Dictionary<long, long> BuyPriceToQuantityMap { get; }
    public Dictionary<long, long> SellPriceToQuantityMap { get; }
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
    public long Price { get; }
    public long Quantity { get; set; }
    public BuySellOperation(string operationType, string orderType, long price, long quantity, string orderId) 
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
        result.Append($"{OrderId} {Price}");
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
    public long NewPrice { get; }
    public long NewQuantity { get; }
    public ModifyOperation(string operationType, string orderId, string buyOrSell, long newPrice, long newQuantity)
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
                return arguments.Length == 5 ? new BuySellOperation(arguments.ElementAt(0).ToUpper(), arguments.ElementAt(1).ToUpper(), long.Parse(arguments.ElementAt(2)),
                    long.Parse(arguments.ElementAt(3)), arguments.ElementAt(4)) : null;
            case OperationType.Cancel:
                return arguments.Length == 2 ? new CancelOperation(arguments.ElementAt(0).ToUpper(), arguments.ElementAt(1)) : null;
            case OperationType.Modify:
                return arguments.Length == 5 ? new ModifyOperation(arguments.ElementAt(0).ToUpper(), arguments.ElementAt(1), arguments.ElementAt(2).ToUpper(), long.Parse(arguments.ElementAt(3)),
                    long.Parse(arguments.ElementAt(4))) : null;
            default: return null;
        }
    }
    private static bool AreArgumentsValid(IReadOnlyCollection<string> arguments)
    {
        if (OperationType.IsValid(arguments.ElementAt(0)))
            return arguments.ElementAt(0) != OperationType.Print || OrderType.IsValid(arguments.ElementAt(1));
        return false;
    }
}


// Dependency injection.
/*public static class DependencyServiceProvider
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
}*/