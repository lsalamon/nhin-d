﻿/* 
 Copyright (c) 2010, NHIN Direct Project
 All rights reserved.

 Authors:
    Umesh Madan     umeshma@microsoft.com
  
Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:

Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.
Neither the name of the The NHIN Direct Project (nhindirect.org). nor the names of its contributors may be used to endorse or promote products derived from this software without specific prior written permission.
THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Net.Mail;
using System.IO;
using NHINDirect.Agent;
using NHINDirect.Agent.Config;
using NHINDirect.Certificates;
using NHINDirect.Diagnostics;
using NHINDirect.Mail;
using NHINDirect.Mime;
using NHINDirect.Config.Store;
using NHINDirect.Config.Client.DomainManager;
using System.Diagnostics;
using CDO;
using ADODB;

namespace NHINDirect.SmtpAgent
{
    public interface ISmtpMessage
    {
        string Sender
        {
            get;
        }
        
        MessageEnvelope GetEnvelope();
        void SetEnvelopeRecipients(NHINDAddressCollection recipients);
        
        void Update(string messageText);
        void Accept();
        void Reject();
        void Abort();
        
        void SaveToFile(string filePath);
    }
    
    public class SmtpAgent
    {
        SmtpAgentSettings m_settings;
        NHINDAgent m_agent;
        DomainPostmasters m_postmasters;
        AgentDiagnostics m_diagnostics;
        MessageRouter m_router;
        ConfigService m_configService;
                
        public SmtpAgent(SmtpAgentSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException();
            }
            
            this.Init(settings);
        }
        
        public NHINDAgent SecurityAgent
        {
            get
            {
                this.VerifyInitialized();
                return m_agent;
            }
        }
        
        public LogFile Log
        {
            get
            {
                return m_diagnostics.Log;
            }
        }
        
        public MessageRouter Router
        {
            get
            {
                return m_router;
            }
        }
        
        void VerifyInitialized()
        {
            if (m_agent == null)
            {
                throw new SmtpAgentException(SmtpAgentError.NotInitialized);
            }
        }

        /// <summary>
        /// Write an informational line to the log
        /// </summary>
        public void LogStatus(string message)
        {
            m_diagnostics.LogStatus(message);
        }

        //---------------------------------------------------
        //
        //  Agent Initialization
        //
        //---------------------------------------------------
        void Init(SmtpAgentSettings settings)
        {
            m_settings = settings;
            m_settings.Validate();
            
            m_diagnostics = new AgentDiagnostics(new LogFile(m_settings.LogSettings.CreateWriter()), m_settings.LogVerbose);
            m_configService = new ConfigService(m_settings);
            
            this.LogStatus("Init_Begin");
            try
            {   
                //
                // First, setup defaults using Xml Config
                //
                this.InitDomains();
                this.InitFolders();
                this.InitRoutes();                
                //
                // Call config service, if any was configured
                //
                this.InitDomainsFromConfigService();
                //
                // Finally, we can agent...
                //
                this.InitAgent();
                this.SubscribeToAgentEvents();
     
                this.LogStatus("Init_End");
            }
            catch (Exception error)
            {
                this.LogStatus("Init_Failed");
                m_diagnostics.LogError(error);
                throw;
            }
        }
        
        void InitAgent()
        {
            this.LogStatus("CreateAgent_Begin");
            
            m_agent = m_settings.CreateAgent();            
            this.LogStatus("CreateAgent_End");            
        }
        
        void InitDomainsFromConfigService()
        {
            if (!m_settings.HasDomainManager)
            {
                this.LogStatus("Domains not loaded from config service");
                return;
            }

            this.LogStatus("InitDomainFromConfigService_Begin");
            
            Domain[] configuredDomains = m_configService.GetDomains(m_settings.Domains);
            if (configuredDomains.IsNullOrEmpty() || configuredDomains.Length != m_settings.Domains.Length)
            {
                throw new SmtpAgentException(SmtpAgentError.ConfiguredDomainsMismatch);
            }
            
            this.InitPostmastersFromConfigService(configuredDomains);    
                        
            this.LogStatus("InitDomainFromConfigService_End");
        }
        
        void InitDomains()
        {
            this.LogStatus("InitDomains_Begin");
            
            this.InitPostmasters();

            this.LogStatus("InitDomains_End");
        }
        
        void InitPostmasters()
        {
            this.LogStatus("InitPostmasters_Begin");

            m_postmasters = new DomainPostmasters();
            m_postmasters.Init(m_settings.Domains, m_settings.Postmasters);

            this.LogStatus("InitPostmasters_End");
        }

        void InitPostmastersFromConfigService(Domain[] domains)
        {
            if (!m_settings.HasAddressManager)
            {
                this.LogStatus("Postmasters not loaded from config service");
                return;
            }
        
            Debug.Assert(m_postmasters != null);
            
            this.LogStatus("InitPostmastersFromConfig_Begin");
            
            AddressManagerClient client = m_settings.AddressManager.CreateAddressManagerClient();
            foreach(Domain domain in domains)
            {
                Address address;
                if (domain.PostmasterID != null && (address = client.GetAddress(domain.PostmasterID.Value)) != null)
                {
                    m_postmasters[domain.Name] = address.ToMailAddress();
                }
            }
                        
            this.LogStatus("InitPostmastersFromConfig_End");
        }
        
        void InitFolders()
        {
            this.LogStatus("InitFolder_Begin");

            m_settings.RawMessage.EnsureFolders();
            m_settings.Incoming.EnsureFolders();
            m_settings.Outgoing.EnsureFolders();
            m_settings.BadMessage.EnsureFolders();

            this.LogStatus("InitFolder_End");        
        }
        
        void InitRoutes()
        {
            m_router = new MessageRouter(m_diagnostics);            
            if (!m_settings.HasRoutes)
            {
                return;
            }
            
            this.LogStatus("InitRoutes_Begin");
            
            m_router.SetRoutes(m_settings.IncomingRoutes);            
            
            this.LogStatus("InitRoutes_End");
        }
        
        void SubscribeToAgentEvents()
        {
            this.LogStatus("SubscribingToEvents_Begin");
            
            m_agent.PreProcessOutgoing += this.OnPreProcessOutgoing;
            m_agent.PreProcessIncoming += this.OnPreProcessIncoming;
            m_agent.ErrorIncoming += m_diagnostics.OnIncomingError;
            m_agent.ErrorOutgoing += m_diagnostics.OnOutgoingError;
            
            DnsCertResolver dnsResolver = m_agent.PublicCertResolver as DnsCertResolver;
            if (dnsResolver != null)
            {
                dnsResolver.Error += m_diagnostics.OnDnsError;
            }

            this.LogStatus("SubscribingToEvents_End");
        }
        
        //---------------------------------------------------
        //
        //  Message Processing
        //
        //---------------------------------------------------
        public void ProcessMessage(CDO.Message message)
        {            
            try
            {
                this.ProcessMessage(new CDOSmtpMessage(message));
            }
            catch
            {
                // Paranoia
                message.AbortMessage();
                throw;
            }
        }

        public void ProcessMessage(ISmtpMessage message)
        {
            try
            {
                this.VerifyInitialized();
                //
                // Preprocessing may involve housekeeping like logging message arrival
                //
                this.PreProcessMessage(message);
                //
                // Let the agent do its thing
                //   
                MessageEnvelope envelope = message.GetEnvelope();
                envelope = this.ProcessEnvelope(message, envelope);
                if (envelope == null)
                {
                    throw new SmtpAgentException(SmtpAgentError.InvalidEnvelopeFromAgent);
                }
                //
                // Internal only messages from the postmaster can be passed through in the clear
                // They are invariably local delivery notification errors
                //
                if (!this.IsInternalPostmasterMessage(envelope))
                {
                    //
                    // Replace the contents of the original message with what the agent gave us
                    //
                    this.UpdateMessageText(message, envelope);
                }
                //
                // We did well...
                //
                this.AcceptMessage(message);
                //
                // We may want want to update logs and do some final post processing
                //
                this.PostProcessMessage(message, envelope);
            }
            catch (Exception ex)
            {
                this.RejectMessage(message);
                m_diagnostics.LogError(ex);
                throw;
            }
        }

        protected virtual void PreProcessMessage(ISmtpMessage message)
        {
            this.LogStatus("Message Received from: " + message.Sender);
            this.CopyMessageToFolder(message, m_settings.RawMessage);
        }
                                
        protected virtual MessageEnvelope ProcessEnvelope(ISmtpMessage message, MessageEnvelope envelope)
        {      
            //
            // Messages from within the domain are always treated as OUTGOING
            // All messages sent by sources OUTSIDE the domain are always treated as INCOMING
            //
            bool isOutgoing = this.SecurityAgent.Domains.IsManaged(envelope.Sender);
            if (isOutgoing)
            {
                envelope = this.ProcessOutgoing(message, envelope);
            }
            else
            {
                envelope = this.ProcessIncoming(message, envelope);
            }                

            if (envelope == null)
            {
                throw new SmtpAgentException(SmtpAgentError.InvalidEnvelopeFromAgent);
            }
                            
            return envelope;
        }

        //---------------------------------------------------
        //
        //  Outgoing
        //
        //---------------------------------------------------

        void OnPreProcessOutgoing(OutgoingMessage message)
        {
            if (!m_settings.HasAddressManager)
            {
                return;
            }
            //
            // Verify that the sender is allowed to send
            //
            Address address = m_configService.GetAddress(message.Sender);
            if (address == null)
            {
                throw new AgentException(AgentError.UntrustedSender);
            }
            
            message.Sender.Tag = address;
        }
        
        public MessageEnvelope ProcessOutgoing(ISmtpMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException();
            }
            
            return this.ProcessOutgoing(message, message.GetEnvelope());
        }
        
        protected virtual MessageEnvelope ProcessOutgoing(ISmtpMessage message, MessageEnvelope envelope)
        {
            envelope = this.SecurityAgent.ProcessOutgoing(envelope);
            this.LogStatus("ProcessedOutgoing");
            return envelope;
        }
        
        //---------------------------------------------------
        //
        //  Incoming
        //
        //---------------------------------------------------
        
        //
        // Event handler called by the agent
        // Here, if configured, we will verify that addresses are real. We don't always have to do that, especially if
        // say we are running as PURELY a gateway. However, if we are set up to e.g. route messages, then...
        //
        void OnPreProcessIncoming(IncomingMessage message)
        {
            if (!m_settings.HasAddressManager)
            {
                return;
            }
            
            message.EnsureRecipientsCategorizedByDomain(this.SecurityAgent.Domains);

            NHINDAddressCollection recipients = message.DomainRecipients;
            Address[] resolved = m_configService.GetAddresses(recipients);
            if (resolved.IsNullOrEmpty())
            {
                throw new AgentException(AgentError.NoRecipients);
            }            
         
            // Remove any addresses that could not be resolved
            // Yes, this is currently n^2, but given the typical # of addresses, cost should be insignificant
            int i = 0;
            while (i < recipients.Count)
            {
                NHINDAddress recipient = recipients[i];
                int iAddress = Array.FindIndex<Address>(resolved, x => x.Match(recipient));
                if (iAddress >= 0)
                {
                    ++i; // Found
                    recipient.Tag = resolved[iAddress];
                }
                else
                {
                    recipients.RemoveAt(i);
                }
            }
        }
        
        public MessageEnvelope ProcessIncoming(ISmtpMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException();
            }
            
            return this.ProcessIncoming(message, message.GetEnvelope());
        }
        
        protected virtual MessageEnvelope ProcessIncoming(ISmtpMessage message, MessageEnvelope envelope)
        {            
            envelope = this.SecurityAgent.ProcessIncoming(envelope);
            this.LogStatus("ProcessedIncoming");
            //
            // Need to update the envelope (if any) to ensure the message is delivered only to trusted valid recipients
            //
            message.SetEnvelopeRecipients(envelope.DomainRecipients);
            
            return envelope;
        }
        
        protected virtual void UpdateMessageText(ISmtpMessage message, MessageEnvelope envelope)
        {
            string messageText = envelope.SerializeMessage();            
            if (string.IsNullOrEmpty(messageText))
            {
                throw new SmtpAgentException(SmtpAgentError.EmptyResultFromAgent);
            }
            
            message.Update(messageText);
        }

        protected virtual void PostProcessMessage(ISmtpMessage message, MessageEnvelope envelope)
        {
            bool relay = true;
            bool isOutgoing = envelope is OutgoingMessage;
            
            if (isOutgoing)
            {
                this.CopyMessageToFolder(message, m_settings.Outgoing);
                relay = m_settings.Outgoing.EnableRelay;
            }
            else
            {
                bool fullyRouted = m_router.Route(message, envelope, this.CopyMessageToFolder);
                this.CopyMessageToFolder(message, m_settings.Incoming);
                relay = fullyRouted ? false : m_settings.Incoming.EnableRelay;
            }
            
            if (!relay)
            {
                message.Abort();
            }
        }
                
        protected virtual void AcceptMessage(ISmtpMessage message)
        {
            message.Accept();
        }
        
        protected virtual void RejectMessage(ISmtpMessage message)
        {
            try
            {
                message.Reject();
                this.LogStatus("Rejected Message");
                this.CopyMessageToFolder(message, m_settings.BadMessage);
            }
            catch
            {
            }
        }
        
        protected virtual void CopyMessageToFolder(ISmtpMessage message, MessageProcessingSettings settings)
        {
            try
            {
                if (settings.HasCopyFolder)
                {
                    string uniqueFileName = Extensions.CreateUniqueFileName();
                    message.SaveToFile(Path.Combine(settings.CopyFolder, uniqueFileName));
                }                
            }
            catch (Exception ex)
            {
                m_diagnostics.LogError(ex);
            }
        }
        
        bool IsInternalPostmasterMessage(MessageEnvelope envelope)
        {
            if (!m_postmasters.IsPostmaster(envelope.Sender))
            {
                return false;
            }

            return (envelope.DomainRecipients.Count == envelope.Recipients.Count);
        }

        bool IsSenderPostmaster(CDO.Message message)
        {
            string sender = message.GetEnvelopeSender();
            if (string.IsNullOrEmpty(sender))
            {
                return false;
            }

            return (Extensions.IsSenderLocalPostmaster(sender) || m_postmasters.IsPostmaster(sender));
        }
    }
}
