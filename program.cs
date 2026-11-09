using System;
using System.Collections.Generic;

// <summary>
// Пользовательский делегат для расчёта итоговой стоимости одной позиции заказа.
// </summary>
// <param name="item">Исходная товарная позиция.</param>
// <param name="transformedUnitPrice">Цена за единицу после преобразования (скидка, налог и т.п.).</param>
// <returns>Стоимость позиции с учётом количества.</returns>
public delegate decimal ItemTotalCalculator(
    OrderItem item,
    decimal transformedUnitPrice);

// <summary>
// Товарная позиция заказа. Неизменяемый класс.
// </summary>
public sealed class OrderItem
{
    // <summary>
    // Создаёт товарную позицию.
    // </summary>
    // <param name="name">Наименование товара.</param>
    // <param name="price">Цена за единицу.</param>
    // <param name="quantity">Количество единиц.</param>
    public OrderItem(string name, decimal price, int quantity)
    {
        Name = name;
        Price = price;
        Quantity = quantity;
    }

    // <summary>Наименование товара.</summary>
    public string Name { get; }

    // <summary>Цена за единицу.</summary>
    public decimal Price { get; }

    // <summary>Количество единиц.</summary>
    public int Quantity { get; }
}

// <summary>
// Аргументы события <see cref="OrderProcessor.OrderProcessed"/>.
// </summary>
public sealed class OrderProcessedEventArgs : EventArgs
{
    // <summary>
    // Создаёт аргументы события о завершении обработки заказа.
    // </summary>
    // <param name="orderId">Номер заказа.</param>
    // <param name="total">Итоговая сумма заказа.</param>
    // <param name="processedItemsCount">Количество обработанных позиций.</param>
    // <param name="processedAt">Момент завершения обработки.</param>
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

    // <summary>Номер заказа.</summary>
    public int OrderId { get; }

    // <summary>Итоговая сумма заказа.</summary>
    public decimal Total { get; }

    // <summary>Количество обработанных позиций.</summary>
    public int ProcessedItemsCount { get; }

    // <summary>Момент завершения обработки.</summary>
    public DateTimeOffset ProcessedAt { get; }
}

// <summary>
// Обработчик заказов интернет-магазина.
// Позволяет настраивать фильтрацию, преобразование цены и расчёт позиции
// через делегаты, а также уведомлять подписчиков о завершении обработки.
// </summary>
public sealed class OrderProcessor
{
    // <summary>
    // Событие, возникающее после успешной обработки заказа.
    // </summary>
    public event EventHandler<OrderProcessedEventArgs>? OrderProcessed;

    // <summary>
    // Обрабатывает заказ и возвращает итоговую стоимость.
    // </summary>
    // <param name="orderId">Номер заказа. Должен быть положительным.</param>
    // <param name="items">Позиции заказа.</param>
    // <param name="shouldProcess">Предикат: включать ли позицию в расчёт.</param>
    // <param name="transformPrice">Преобразование цены за единицу.</param>
    // <param name="calculateItemTotal">Расчёт стоимости одной позиции.</param>
    // <param name="log">Необязательный журнал этапов обработки.</param>
    // <returns>Итоговая стоимость заказа.</returns>
    public decimal Process(
        int orderId,
        IEnumerable<OrderItem> items,
        Predicate<OrderItem> shouldProcess,
        Func<decimal, decimal> transformPrice,
        ItemTotalCalculator calculateItemTotal,
        Action<string>? log = null)
    {
        // --- Проверка аргументов ---
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
            // Защита от null-элементов в последовательности.
            ArgumentNullException.ThrowIfNull(item);

            // --- Валидация данных позиции ---
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

            // --- Фильтрация ---
            if (!shouldProcess(item))
            {
                log?.Invoke($"Товар '{item.Name}' исключён из расчёта.");
                continue;
            }

            // --- Преобразование цены ---
            decimal transformedPrice = transformPrice(item.Price);

            log?.Invoke(
                $"Цена товара '{item.Name}' преобразована: " +
                $"{item.Price:C} -> {transformedPrice:C}.");

            // --- Расчёт стоимости позиции ---
            decimal itemTotal = calculateItemTotal(item, transformedPrice);

            total += itemTotal;
            processedItemsCount++;

            log?.Invoke(
                $"Стоимость позиции '{item.Name}': {itemTotal:C}.");
        }

        // --- Формирование и вызов события ---
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

    // <summary>
    // Вызывает событие <see cref="OrderProcessed"/> с изоляцией ошибок
    // каждого подписчика: исключение одного обработчика не прерывает остальные.
    // </summary>
    // <param name="e">Аргументы события.</param>
    private void OnOrderProcessed(OrderProcessedEventArgs e)
    {
        // Снимок делегата: защита от изменения списка подписчиков во время обхода.
        EventHandler<OrderProcessedEventArgs>? handlers = OrderProcessed;

        if (handlers is null)
        {
            return;
        }

        // Обход цепочки вручную — иначе исключение первого обработчика
        // прервало бы вызов остальных.
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

// <summary>
// Точка входа приложения: демонстрация работы <see cref="OrderProcessor"/>.
// </summary>
public static class Program
{
    // <summary>
    // Запускает демонстрационную обработку заказа.
    // </summary>
    public static void Main()
    {
        // --- Исходные данные ---
        var items = new List<OrderItem>
        {
            new("Ноутбук", 75000m, 1),
            new("Мышь", 1500m, 2),
            new("Клавиатура", 4000m, 1),
            new("Коврик", 500m, 1)
        };

        var processor = new OrderProcessor();

        // --- Журнал: мультикаст-делегат Action<string> ---
        // Сначала пишем как есть, затем дублируем с префиксом [LOG].
        Action<string> logger = Console.WriteLine;

        logger += static message =>
            Console.WriteLine($"[LOG] {message}");

        // --- Замыкание: лямбда захватывает discountRate ---
        decimal discountRate = 0.10m;

        Func<decimal, decimal> transformPrice =
            price => price * (1m - discountRate);

        // --- static-лямбда: фильтр по минимальной цене ---
        Predicate<OrderItem> shouldProcess =
            static item => item.Price >= 1000m;

        // --- static-лямбда: расчёт стоимости позиции ---
        ItemTotalCalculator calculateItemTotal =
            static (item, transformedUnitPrice) =>
                transformedUnitPrice * item.Quantity;

        // --- Обработчики события ---
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

        // Намеренно «сломанный» подписчик — проверяем устойчивость цепочки.
        EventHandler<OrderProcessedEventArgs> errorHandler =
            static (sender, e) =>
            {
                throw new InvalidOperationException(
                    "Тестовая ошибка обработчика.");
            };

        // --- Подписка ---
        processor.OrderProcessed += consoleHandler;
        processor.OrderProcessed += notificationHandler;
        processor.OrderProcessed += errorHandler;

        // --- Обработка заказа ---
        decimal total = processor.Process(
            orderId: 1001,
            items: items,
            shouldProcess: shouldProcess,
            transformPrice: transformPrice,
            calculateItemTotal: calculateItemTotal,
            log: logger);

        Console.WriteLine();
        Console.WriteLine($"Итог к оплате: {total:C}");

        // --- Отписка ---
        processor.OrderProcessed -= consoleHandler;
        processor.OrderProcessed -= notificationHandler;
        processor.OrderProcessed -= errorHandler;
    }
}
