
using System;
using System.Collections.Generic;

public delegate decimal ItemTotalCalculator(
    OrderItem item,
    decimal transformedUnitPrice);

public sealed class OrderItem
{
    public OrderItem(string name, decimal price, int quantity)
    {
        Name = name;
        Price = price;
        Quantity = quantity;
    }

    public string Name { get; }
    public decimal Price { get; }
    public int Quantity { get; }
}

public sealed class OrderProcessedEventArgs : EventArgs
{
    public OrderProcessedEventArgs(
        int orderId,
        decimal total,
        int processedItemsCount,
        DateTimeOffset processedAt)
    {
        OrderId = orderId;
        Total = total;
        ProcessedItemsCount = processedItemsCount;
        ProcessedAt = processedAt;
    }

    public int OrderId { get; }
    public decimal Total { get; }
    public int ProcessedItemsCount { get; }
    public DateTimeOffset ProcessedAt { get; }
}

public sealed class OrderProcessor
{
    public event EventHandler<OrderProcessedEventArgs>? OrderProcessed;

    public decimal Process(
        int orderId,
        IEnumerable<OrderItem> items,
        Predicate<OrderItem> shouldProcess,
        Func<decimal, decimal> transformPrice,
        ItemTotalCalculator calculateItemTotal,
        Action<string>? log = null)
    {
        if (orderId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(orderId));
        }

        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(shouldProcess);
        ArgumentNullException.ThrowIfNull(transformPrice);
        ArgumentNullException.ThrowIfNull(calculateItemTotal);

        log?.Invoke($"Начало обработки заказа №{orderId}.");

        decimal total = 0m;
        int processedItemsCount = 0;

        foreach (OrderItem item in items)
        {
            ArgumentNullException.ThrowIfNull(item);

            if (item.Price < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(items),
                    $"Цена товара '{item.Name}' не может быть отрицательной.");
            }

            if (item.Quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(items),
                    $"Количество товара '{item.Name}' должно быть положительным.");
            }

            if (!shouldProcess(item))
            {
                log?.Invoke($"Товар '{item.Name}' исключён из расчёта.");
                continue;
            }

            decimal transformedPrice = transformPrice(item.Price);

            log?.Invoke(
                $"Цена товара '{item.Name}' преобразована: " +
                $"{item.Price:C} -> {transformedPrice:C}.");

            decimal itemTotal = calculateItemTotal(item, transformedPrice);

            total += itemTotal;
            processedItemsCount++;

            log?.Invoke(
                $"Стоимость позиции '{item.Name}': {itemTotal:C}.");
        }

        var eventArgs = new OrderProcessedEventArgs(
            orderId,
            total,
            processedItemsCount,
            DateTimeOffset.UtcNow);

        OnOrderProcessed(eventArgs);

        log?.Invoke(
            $"Заказ №{orderId} обработан. Итоговая сумма: {total:C}.");

        return total;
    }

    private void OnOrderProcessed(OrderProcessedEventArgs e)
    {
        EventHandler<OrderProcessedEventArgs>? handlers = OrderProcessed;

        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<OrderProcessedEventArgs> handler
                 in handlers.GetInvocationList())
        {
            try
            {
                handler(this, e);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(
                    $"Ошибка обработчика: {exception.Message}");
            }
        }
    }
}

public static class Program
{
    public static void Main()
    {
        var items = new List<OrderItem>
        {
            new("Ноутбук", 75000m, 1),
            new("Мышь", 1500m, 2),
            new("Клавиатура", 4000m, 1),
            new("Коврик", 500m, 1)
        };

        var processor = new OrderProcessor();

        Action<string> logger = Console.WriteLine;

        logger += static message =>
            Console.WriteLine($"[LOG] {message}");

        decimal discountRate = 0.10m;

        Func<decimal, decimal> transformPrice =
            price => price * (1m - discountRate);

        Predicate<OrderItem> shouldProcess =
            static item => item.Price >= 1000m;

        ItemTotalCalculator calculateItemTotal =
            static (item, transformedUnitPrice) =>
                transformedUnitPrice * item.Quantity;

        EventHandler<OrderProcessedEventArgs> consoleHandler =
            (sender, e) =>
            {
                Console.WriteLine();
                Console.WriteLine("=== Заказ завершён ===");
                Console.WriteLine($"Номер заказа: {e.OrderId}");
                Console.WriteLine($"Обработано товаров: {e.ProcessedItemsCount}");
                Console.WriteLine($"Итоговая сумма: {e.Total:C}");
                Console.WriteLine($"Время обработки: {e.ProcessedAt}");
            };

        EventHandler<OrderProcessedEventArgs> notificationHandler =
            static (sender, e) =>
            {
                Console.WriteLine(
                    $"Уведомление: заказ №{e.OrderId} успешно обработан.");
            };

        EventHandler<OrderProcessedEventArgs> errorHandler =
            static (sender, e) =>
            {
                throw new InvalidOperationException(
                    "Тестовая ошибка обработчика.");
            };

        processor.OrderProcessed += consoleHandler;
        processor.OrderProcessed += notificationHandler;
        processor.OrderProcessed += errorHandler;

        decimal total = processor.Process(
            orderId: 1001,
            items: items,
            shouldProcess: shouldProcess,
            transformPrice: transformPrice,
            calculateItemTotal: calculateItemTotal,
            log: logger);

        Console.WriteLine();
        Console.WriteLine($"Итог к оплате: {total:C}");

        processor.OrderProcessed -= consoleHandler;
        processor.OrderProcessed -= notificationHandler;
        processor.OrderProcessed -= errorHandler;
    }
}
