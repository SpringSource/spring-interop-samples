#region License

/*
 * Copyright 2002-2009 the original author or authors.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#endregion

using System;
using System.Collections;
using System.Windows.Forms;
using Common.Logging;
using Spring.Context.Support;
using Spring.Interop.StockTraderSample.Common.Data;
using Spring.Messaging.Amqp.Core;
using Spring.Messaging.Amqp.Rabbit.Connection;
using Spring.Messaging.Amqp.Rabbit.Core;
using Spring.Messaging.Amqp.Rabbit.Listener;

namespace Spring.Interop.StockTraderSample.Client.UI
{
    /// <summary>
    /// 
    /// </summary>
    /// <author>Mark Pollack</author>
    /// <author>Don McRae</author>
    public partial class StockForm : Form
    {
        #region Logging Definition

        private static readonly ILog log = LogManager.GetLogger(typeof(StockForm));

        #endregion

        private StockController _stockController;
        private string _currentBinding;
        private const decimal DefaultTradeRequestQuantity = 1;
        private const string DefaultAccountName = "ACCT-123";
        private const string DefaultRoutingKey = "app.stock.quotes.nasdaq.*";

        public StockForm()
        {
            InitializeComponent();
            _stockController = ContextRegistry.GetContext()["StockController"] as StockController;
            _stockController.StockForm = this;
        }

        public StockController Controller
        {
            set { _stockController = value; }
        }

        private void OnSendTradeRequest(object sender, EventArgs e)
        {
            _stockController.SendTradeRequest(tradeRequestTickerSymbol.Text, Convert.ToInt32(tradeQuantityNumericUpDown.Value), accountNameTextBox.Text, buyRadioButton.Checked);
            tradeRequestTickerSymbol.Text = "Request Pending...";
            log.Info("Sent trade request.");
        }

        public void UpdateTrade(TradeResponse trade)
        {

            Invoke(new MethodInvoker(
                       delegate
                       {
                           tradeRequestTickerSymbol.Text = string.Empty;

                           if (trade.Error)
                           {
                               var msg = string.Format("Error processing {0} shares of {1}:\n{2}", trade.Quantity, trade.Ticker, trade.ErrorMessage);
                               MessageBox.Show(msg, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                           }
                           else
                           {
                               var purchasedOrSold = trade.BuyRequest ? "Purchased" : "Sold";

                               var msg = string.Format("Confirmed. {0} {1} shares of {2}\nfor a total cost of {3} ", purchasedOrSold, trade.Quantity, trade.Ticker, trade.Price);
                               MessageBox.Show(msg, "Trade Completed", MessageBoxButtons.OK, MessageBoxIcon.Information);
                           }
                       }));
        }

        public void UpdateMarketData(Quote quote)
        {
            Invoke(new MethodInvoker(
                       delegate
                       {
                           marketDataListBox.Items.Insert(0, string.Format("{0}.{1} {2}", quote.Stock.StockExchange, quote.Stock.Ticker, quote.Price));

                           //ensure that there are never more than 50 entries in the listbox (to prevent endlessly-growing data)
                           if (marketDataListBox.Items.Count == 50)
                           {
                               marketDataListBox.Items.RemoveAt(49);
                           }

                       }));
        }

        private void btnApply_Click(object sender, EventArgs e)
        {
            RebindQueue(txtRoutingKey.Text);
            marketDataListBox.Items.Clear();
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            RebindQueue(string.Empty);
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            var ctx = ContextRegistry.GetContext();
            var listener = ctx.GetObject("MessageListenerContainer") as SimpleMessageListenerContainer;

            if (!listener.IsRunning)
            {
                listener.Start();
            }

            tradeOperationsGroupBox.Enabled = true;

            RebindQueue(txtRoutingKey.Text);
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            marketDataListBox.Items.Clear();
        }

        private void StockForm_Load(object sender, EventArgs e)
        {
            accountNameTextBox.Text = DefaultAccountName;
            tradeQuantityNumericUpDown.Value = DefaultTradeRequestQuantity;
            txtRoutingKey.Text = DefaultRoutingKey;

            tradeOperationsGroupBox.Enabled = false;

            try
            {
                using (IConnectionFactory connectionFactory = new CachingConnectionFactory())
                {
                    IAmqpAdmin amqpAdmin = new RabbitAdmin(connectionFactory);

                    TopicExchange mktDataExchange = new TopicExchange("app.stock.marketdata", false, false);
                    amqpAdmin.DeclareExchange(mktDataExchange);
                    Spring.Messaging.Amqp.Core.Queue mktDataQueue = new Spring.Messaging.Amqp.Core.Queue("app.stock.marketdata");
                    amqpAdmin.DeclareQueue(mktDataQueue);

                    //Create the Exchange for MarketData Requests if it does not already exist.
                    //amqpAdmin.DeclareBinding(BindingBuilder.Bind(mktDataQueue).To(mktDataExchange).With(_currentBinding));
                    //Set up initial binding
                    RebindQueue(DefaultRoutingKey);
                }
            }
            catch (Exception ex)
            {
                log.ErrorFormat("Uncaught application exception.", ex);
            }
        }

        private void RebindQueue(string routingKey)
        {
            try
            {
                var ctx = ContextRegistry.GetContext();
                var factory = ctx.GetObject("ConnectionFactory") as IConnectionFactory;

                IAmqpAdmin amqpAdmin = new RabbitAdmin(factory);

                TopicExchange mktDataExchange = new TopicExchange("app.stock.marketdata", false, false);
                Spring.Messaging.Amqp.Core.Queue mktDataQueue = new Spring.Messaging.Amqp.Core.Queue("app.stock.marketdata");

                if (!string.IsNullOrEmpty(_currentBinding))
                    amqpAdmin.RemoveBinding(BindingBuilder.Bind(mktDataQueue).To(mktDataExchange).With(_currentBinding));

                _currentBinding = routingKey;
                if (!string.IsNullOrEmpty(_currentBinding))
                {
                    amqpAdmin.DeclareBinding(BindingBuilder.Bind(mktDataQueue).To(mktDataExchange).With(_currentBinding));
                    txtRoutingKey.Text = _currentBinding;
                }


            }
            catch (Exception ex)
            {
                log.ErrorFormat("Uncaught application exception.", ex);
            }
        }

        private void StockForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            RebindQueue(string.Empty);
        }

        private void currentPositionsButton_Click(object sender, EventArgs e)
        {
            var positionsForm = new PositionsForm(accountNameTextBox.Text);
            positionsForm.ShowDialog();
        }

    }
}