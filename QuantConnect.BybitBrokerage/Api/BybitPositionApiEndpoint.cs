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
using QuantConnect.Brokerages;
using QuantConnect.BybitBrokerage.Models;
using QuantConnect.BybitBrokerage.Models.Enums;
using QuantConnect.Orders;
using QuantConnect.Securities;
using OrderType = QuantConnect.Orders.OrderType;

namespace QuantConnect.BybitBrokerage.Api;

/// <summary>
/// Bybit position api endpoint implementation
/// <seealso href="https://bybit-exchange.github.io/docs/v5/position"/>
/// </summary>
public class BybitPositionApiEndpoint : BybitApiEndpoint
{
    private readonly BybitMarketApiEndpoint _marketApi;

    /// <summary>
    /// Initializes a new instance of the <see cref="BybitPositionApiEndpoint"/> class
    /// </summary>
    /// <param name="marketApi">The market API used to get current ticker information</param>
    /// <param name="symbolMapper">The symbol mapper</param>
    /// <param name="apiPrefix">The api prefix</param>
    /// <param name="securityProvider">The security provider</param>
    /// <param name="apiClient">The Bybit api client</param>
    public BybitPositionApiEndpoint(BybitMarketApiEndpoint marketApi, ISymbolMapper symbolMapper, string apiPrefix,
        ISecurityProvider securityProvider,
        BybitApiClient apiClient) : base(symbolMapper, apiPrefix, securityProvider, apiClient)
    {
        _marketApi = marketApi;
    }

    /// <summary>
    /// Query real-time position data, such as position size, cumulative realizedPNL.
    /// </summary>
    /// <param name="category">The product category</param>
    /// <returns>A list of all open positions in the current category</returns>
    public IEnumerable<BybitPositionInfo> GetPositions(BybitProductCategory category)
    {
        if (category == BybitProductCategory.Spot) return Array.Empty<BybitPositionInfo>();

        var parameters = new List<KeyValuePair<string, string>>();

        if (category == BybitProductCategory.Linear)
        {
            parameters.Add(KeyValuePair.Create("settleCoin", "USDT"));
        }

        return FetchAll<BybitPositionInfo>("/position/list", category, 200, parameters, true);
    }

    /// <summary>
    /// It supports to switch the position mode for USDT perpetual and Inverse futures.
    /// If you are in one-way Mode, you can only open one position on Buy or Sell side. If you are in hedge mode, you can open both Buy and Sell side positions simultaneously.
    /// </summary>
    /// <param name="category">The product category</param>
    /// <param name="symbol">The symbol for which the mode should be changed</param>
    /// <param name="mode">The mode which should be set</param>
    public void SwitchPositionMode(BybitProductCategory category, Symbol symbol, PositionMode mode)
    {
        var ticker = SymbolMapper.GetBrokerageSymbol(symbol);
        var requestBody = new
        {
            category,
            mode = (int)mode,
            symbol = ticker
        };

        ExecutePostRequest<ByBitResponse>("/position/switch-mode", requestBody);
    }


    /// <summary>
    /// Set the take profit, stop loss or trailing stop for the position.
    /// </summary>
    public void SetTradingStop(BybitProductCategory category, Order order)
    {
        if (category is not (BybitProductCategory.Linear or BybitProductCategory.Inverse))
        {
            throw new NotSupportedException($"Category {category} is not supported. Only linear and inverse");
        }

        if (order.Type != OrderType.TrailingStop)
        {
            throw new NotSupportedException("This endpoint should only be used for tailing-stop orders");
        }

        var trailingOrder = (TrailingStopOrder)order;

        var distance = trailingOrder.TrailingAmount;
        var ticker = SymbolMapper.GetBrokerageSymbol(order.Symbol);

        if (trailingOrder.TrailingAsPercentage)
        {
            var security = SecurityProvider.GetSecurity(order.Symbol);
            var tickSize = security.SymbolProperties.MinimumPriceVariation;
            var lastPrice = security.Price != 0 ? security.Price :_marketApi.GetTicker(category, ticker).LastPrice ?? 0;
            distance = Math.Round((lastPrice  * trailingOrder.TrailingAmount)/tickSize) * tickSize;
        }


        var requestBody = new
        {
            category = category,
            Symbol = ticker,
            trailingStop = distance,
            positionIdx = (int)PositionIndex.OneWayMode,
            slSize = order.Quantity,
            activePrice = trailingOrder.StopPrice == 0 ? default(decimal?) : trailingOrder.StopPrice,
        };

        ExecutePostRequest<ByBitResponse>("/position/trading-stop", requestBody);
    }
}