/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using QuantConnect.BybitBrokerage.Models.Enums;
using QuantConnect.Interfaces;
using QuantConnect.Logging;
using QuantConnect.Orders;
using QuantConnect.Tests;
using QuantConnect.Tests.Brokerages;

namespace QuantConnect.BybitBrokerage.Tests;

[TestFixture, Explicit("Requires valid credentials to be setup and run outside USA")]
public partial class BybitFuturesBrokerageTests : BybitBrokerageTests
{
    private static Symbol BTCUSDT = Symbol.Create("BTCUSDT", SecurityType.CryptoFuture, "bybit");
    protected override Symbol Symbol { get; } = BTCUSDT;
    protected override SecurityType SecurityType => SecurityType.Future;
    protected override BybitProductCategory Category => BybitProductCategory.Linear;
    protected override decimal GetDefaultQuantity() => 0.001m;


    /// <summary>
    /// Provides the data required to test each order type in various cases
    /// </summary>
    private static TestCaseData[] OrderParameters()
    {
        return new[]
        {
            new TestCaseData(new MarketOrderTestParameters(BTCUSDT)).SetName("MarketOrder"),
            new TestCaseData(new LimitOrderTestParameters(BTCUSDT, 50000m, 10000m)).SetName("LimitOrder"),
            new TestCaseData(new StopMarketOrderTestParameters(BTCUSDT, 50000m, 10000m)).SetName("StopMarketOrder"),
            new TestCaseData(new StopLimitOrderTestParameters(BTCUSDT, 50000m, 10000m)).SetName("StopLimitOrder"),
            new TestCaseData(new LimitIfTouchedOrderTestParameters(BTCUSDT, 50000m, 20000)).SetName(
                "LimitIfTouchedOrder")
        };
    }

    public class TrailingStopOrderTestParameters : StopMarketOrderTestParameters
    {
        public TrailingStopOrderTestParameters(Symbol symbol, decimal highLimit, decimal lowLimit, IOrderProperties properties = null, OrderSubmissionData orderSubmissionData = null) : base(symbol, highLimit, lowLimit, properties, orderSubmissionData)
        {
        }

        public override Order CreateShortOrder(decimal quantity)
        {


            var order = new  TrailingStopOrder(Symbol, -Math.Abs(quantity), 10, false, DateTime.Now, null, Properties);
            SetSubmissionData(order);
            return order;
        }

        public override Order CreateLongOrder(decimal quantity)
        {
            var order = new  TrailingStopOrder(Symbol, Math.Abs(quantity), 10, false, DateTime.Now, null, Properties);
            SetSubmissionData(order);
            return order;
        }


        private void SetSubmissionData(Order order)
        {
            var prop = typeof(Order).GetProperty(nameof(order.OrderSubmissionData), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            prop.SetValue(order, OrderSubmissionData);
        }
        
    }

    [Test, TestCaseSource(nameof(OrderParameters))]
    public override void CancelOrders(OrderTestParameters parameters)
    {
        base.CancelOrders(parameters);
    }

    [Test, TestCaseSource(nameof(OrderParameters))]
    public override void LongFromZero(OrderTestParameters parameters)
    {
        base.LongFromZero(parameters);
    }

    [Test, TestCaseSource(nameof(OrderParameters))]
    public override void CloseFromLong(OrderTestParameters parameters)
    {
        base.CloseFromLong(parameters);
    }

    [Test, TestCaseSource(nameof(OrderParameters))]
    public override void ShortFromZero(OrderTestParameters parameters)
    {
        base.ShortFromZero(parameters);
    }

    [Test, TestCaseSource(nameof(OrderParameters))]
    public override void CloseFromShort(OrderTestParameters parameters)
    {
        base.CloseFromShort(parameters);
    }

    [Test, TestCaseSource(nameof(OrderParameters))]
    public override void ShortFromLong(OrderTestParameters parameters)
    {
        base.ShortFromLong(parameters);
    }

    [Test, TestCaseSource(nameof(OrderParameters))]
    public override void LongFromShort(OrderTestParameters parameters)
    {
        base.LongFromShort(parameters);
    }

    [Test]
    public void CancelTrailingStopOrder()
    {
        var order = new MarketOrder(Symbol, GetDefaultQuantity(), DateTime.Today);
        PlaceOrderWaitForStatus(order);

        var stop = new TrailingStopOrder(Symbol, -GetDefaultQuantity(), 100, false, DateTime.Now);
        PlaceOrderWaitForStatus(stop, Orders.OrderStatus.Submitted);
        
        var canceledOrderStatusEvent = new ManualResetEvent(false);
        EventHandler<List<OrderEvent>> orderStatusCallback = (sender, fills) =>
        {
            if (fills[0].Status == Orders.OrderStatus.Canceled)
            {
                canceledOrderStatusEvent.Set();
            }
        };
        Brokerage.OrdersStatusChanged += orderStatusCallback;
        var cancelResult = false;
        try
        {
            cancelResult = Brokerage.CancelOrder(stop);
        }
        catch (Exception exception)
        {
            Log.Error(exception);
        }

        Assert.AreEqual(true, cancelResult);

        
            // We expect the OrderStatus.Canceled event
            canceledOrderStatusEvent.WaitOneAssertFail(1000 * 5, "Order timedout to cancel");
        

        var openOrders = Brokerage.GetOpenOrders();
        var cancelledOrder = openOrders.FirstOrDefault(x => x.Id == stop.Id);
        Assert.IsNull(cancelledOrder);

        canceledOrderStatusEvent.Reset();

        var cancelResultSecondTime = false;
        try
        {
            cancelResultSecondTime = Brokerage.CancelOrder(stop);
        }
        catch (Exception exception)
        {
            Log.Error(exception);
        }
        Assert.AreEqual(IsCancelAsync(), cancelResultSecondTime);
        // We do NOT expect the OrderStatus.Canceled event
        Assert.IsFalse(canceledOrderStatusEvent.WaitOne(new TimeSpan(0, 0, 10)));

        Brokerage.OrdersStatusChanged -= orderStatusCallback;
        
        
    }

    [Test]
    public void FillTrailingStopOrderShort()
    {
        Log.DebuggingEnabled = true;
        var order = new MarketOrder(Symbol, GetDefaultQuantity(), DateTime.Today);
        PlaceOrderWaitForStatus(order);

        var stop = new TrailingStopOrder(Symbol, -GetDefaultQuantity(), 5, false, DateTime.Now);
        PlaceOrderWaitForStatus(stop, Orders.OrderStatus.Filled, 600);
    }
    
    [Test]
    public void FillTrailingStopOrderPercentageShort()
    {
        Log.DebuggingEnabled = true;
        var order = new MarketOrder(Symbol, GetDefaultQuantity(), DateTime.Today);
        PlaceOrderWaitForStatus(order);

        var stop = new TrailingStopOrder(Symbol, -GetDefaultQuantity(), 0.00001m, true, DateTime.Now);
        PlaceOrderWaitForStatus(stop, Orders.OrderStatus.Filled, 600);
    }
    
    [Test]
    public void FillTrailingStopOrderPercentageLong()
    {
        Log.DebuggingEnabled = true;
        var order = new MarketOrder(Symbol, GetDefaultQuantity(), DateTime.Today);
        PlaceOrderWaitForStatus(order);

        var stop = new TrailingStopOrder(Symbol, -GetDefaultQuantity(), 0.00001m, true, DateTime.Now);
        PlaceOrderWaitForStatus(stop, Orders.OrderStatus.Filled, 600);
    }

    [Test]
    public void FillTrailingStopOrderLong()
    {
        Log.DebuggingEnabled = true;
        var order = new MarketOrder(Symbol, -GetDefaultQuantity(), DateTime.Today);
        PlaceOrderWaitForStatus(order);

        var stop = new TrailingStopOrder(Symbol, GetDefaultQuantity(), 5, false, DateTime.Now);
        PlaceOrderWaitForStatus(stop, Orders.OrderStatus.Filled, 600);
    }
    
    
    [Ignore("The brokerage is shared between different product categories, therefore this test is only required in the base class")]
    public override void GetAccountHoldings()
    {
        base.GetAccountHoldings();
    }
}