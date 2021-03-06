﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Exchange.Data.Transport;
using Microsoft.Exchange.Data.Transport.Routing;
using SprintMarketing.C28.ExchangeAgent.API;
using SprintMarketing.C28.ExchangeAgent.API.Models;

using System.Text;
using System.Globalization;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Configuration.Install;
using System.Linq.Expressions;
using Microsoft.Exchange.Data.Transport.Email;
using Microsoft.Win32;
using Microsoft.Exchange.Data.Transport.Smtp;
using SprintMarketing.C28.ExchangeAgent.converters;

namespace SprintMarketing.C28.ExchangeAgent {
    public class C28AgentFactory : RoutingAgentFactory
    {
        public override RoutingAgent CreateAgent(SmtpServer server)
        {
            return new C28RoutingAgent();
        }
    }


    public class C28RoutingAgent : RoutingAgent
    {
        public C28RoutingAgent()
        {
            OnResolvedMessage += SprintRoutingAgent_OnResolvedMessage;
            //OnSubmittedMessage += rewriteEmailAddress;
        }
        
        void SprintRoutingAgent_OnResolvedMessage(ResolvedMessageEventSource source, QueuedMessageEventArgs e)
        {
            string rcpts = "";
            foreach (var r in e.MailItem.Message.To)
            {
                rcpts += r.SmtpAddress + ", ";
            }
            foreach (var r in e.MailItem.Message.Cc)
            {
                rcpts += r.SmtpAddress + ", ";
            }
            foreach (var r in e.MailItem.Message.Bcc)
            {
                rcpts += r.SmtpAddress + ", ";
            }

            C28Logger.Info(C28Logger.C28LoggerType.AGENT, "Message id = " + e.MailItem.Message.MessageId + ", recipients = " + rcpts + "; enveloppe id = " + e.MailItem.EnvelopeId);

            try
            {
                var context = C28AgentManager.getInstance().getContext();
                if (!context.isOutbound(e.MailItem))
                {
                    return; // dont handle inbound messages
                }

                if (!context.shouldBeHandledByC28(e.MailItem))
                {
                    C28Logger.Info(C28Logger.C28LoggerType.AGENT,
                        String.Format("Message from '{0}'. Domain is not present, ignoring.",
                            e.MailItem.FromAddress.ToString()));
                    return;
                }

                C28ExchangeDomain domain = context.exchangeData.getDomain(e.MailItem.FromAddress.DomainPart);
                if (domain == null)
                {
                    C28Logger.Info(C28Logger.C28LoggerType.AGENT, String.Format("Domain '{0}' could not be found... Skipping entry", e.MailItem.FromAddress.DomainPart));
                    return;
                }

                RoutingAddress fromAddr = e.MailItem.FromAddress;
                C28Logger.Debug(C28Logger.C28LoggerType.AGENT,
                    String.Format("Domain '{0}' is set to be overriden to routing domain '{1}'", fromAddr.DomainPart,
                        domain.connector_override));

                bool allOnSameExchangeDomain = true;
                foreach (var recp in e.MailItem.Recipients)
                {
                    allOnSameExchangeDomain = allOnSameExchangeDomain &&
                        recp.Address.DomainPart.ToLower() == domain.domain.ToLower();
                }

                foreach (var recp in e.MailItem.Recipients)
                {
                    if (allOnSameExchangeDomain &&
                        domain.same_domain_action == "LocalDelivery")
                    {
                        C28Logger.Debug(C28Logger.C28LoggerType.AGENT,
                            String.Format(
                                "Message from '{0}' to '{1}' was ignored; all recipients are on the same internal domain.",
                                fromAddr.ToString(), recp.Address.ToString()));
                        continue;
                    }
                    if (recp.RecipientCategory == RecipientCategory.InSameOrganization &&
                        context.exchangeData.currentClient.same_organization_action == "LocalDelivery")
                    {
                        C28Logger.Debug(C28Logger.C28LoggerType.AGENT,
                            String.Format("Recipient '{0}' is in the same organization; ignoring.",
                                recp.Address.ToString()));
                        continue;
                    }
                    if (domain.isRecipientExcluded(recp.Address.ToString()))
                    {
                        C28Logger.Debug(C28Logger.C28LoggerType.AGENT,
                            String.Format("Recipient '{0}' matched an excluded recipient pattern; ignoring.",
                            recp.Address.ToString()));
                        continue;
                    }

                    recp.SetRoutingOverride(new RoutingDomain(domain.connector_override));
                }

                return;
            }
            catch (Exception ee)
            {
                C28Logger.Fatal(C28Logger.C28LoggerType.AGENT, "Unhandled Exception", ee);
            }
        }
    }
}