/*
 * To change this template, choose Tools | Templates
 * and open the template in the editor.
 */
package org.nhind.xdr;

import ihe.iti.xds_b._2007.DocumentRepositoryPortType;
import ihe.iti.xds_b._2007.DocumentRepositoryService;
import ihe.iti.xds_b._2007.ProvideAndRegisterDocumentSetRequestType;
import ihe.iti.xds_b._2007.ProvideAndRegisterDocumentSetRequestType.Document;

import java.io.ByteArrayOutputStream;
import java.util.ArrayList;
import java.util.List;
import java.util.UUID;
import java.util.logging.Logger;

import javax.activation.DataHandler;
import javax.activation.DataSource;
import javax.mail.util.ByteArrayDataSource;
import javax.naming.InitialContext;
import javax.xml.namespace.QName;
import javax.xml.ws.BindingProvider;
import javax.xml.ws.WebServiceContext;
import javax.xml.ws.soap.MTOMFeature;
import javax.xml.ws.soap.SOAPBinding;

import oasis.names.tc.ebxml_regrep.xsd.lcm._3.SubmitObjectsRequest;
import oasis.names.tc.ebxml_regrep.xsd.rs._3.RegistryResponseType;

import org.nhind.util.XMLUtils;
import org.nhind.xdm.SMTPMailClient;

/**
 *
 * @author Vince
 */
public class DocumentRepositoryAbstract {


    protected WebServiceContext mywscontext;
    protected String endpoint = null;
    protected String messageId = null;
    protected String relatesTo = null;
    protected String action = null;
    protected String to = null;
    protected String thisHost = null;
    protected String remoteHost = null;
    protected String pid = null;
    protected String from = null;
    protected String suffix = null;
    private String replyEmail = null;

    @SuppressWarnings("unused")
    private String docObjectId = null; // TODO: is this needed?
    
    String thincoid = "2.16.840.1.113883.3.402";

    private static final Logger LOGGER = Logger.getLogger(DocumentRepositoryAbstract.class.getPackage().getName());
    
    public RegistryResponseType provideAndRegisterDocumentSet(ProvideAndRegisterDocumentSetRequestType prdst) throws Exception {
        RegistryResponseType resp = null;
        try {
            getHeaderData();
            
            @SuppressWarnings("unused")
            InitialContext ctx = new InitialContext();
            
            QName qname = new QName("urn:ihe:iti:xds-b:2007", "ProvideAndRegisterDocumentSet_bRequest");
            String body = XMLUtils.marshal(qname, prdst, ihe.iti.xds_b._2007.ObjectFactory.class);
            QName sname = new QName("urn:ihe:iti:xds-b:2007", "SubmitObjectsRequest");
            SubmitObjectsRequest sor = prdst.getSubmitObjectsRequest();
            String meta = XMLUtils.marshal(sname, sor, ihe.iti.xds_b._2007.ObjectFactory.class);
            List<String> forwards = provideAndRegister(prdst);

            String rmessageId = fowardMessage(4, body, forwards, replyEmail, prdst, messageId, endpoint, suffix, meta);

            resp = getRepositoryProvideResponse(rmessageId);

            relatesTo = messageId;
            action = "urn:ihe:iti:2007:ProvideAndRegisterDocumentSet-bResponse";
            messageId = rmessageId;
            to = endpoint;
            setHeaderData();
        } catch (Exception e) {
            e.printStackTrace();
            throw (e);
        }

        return resp;
    }

    protected List<String> provideAndRegister(ProvideAndRegisterDocumentSetRequestType prdst) throws Exception {
        List<String> forwards = null;

        try {
            @SuppressWarnings("unused")
            InitialContext ctx = new InitialContext();

            DocumentRegistry drr = new DocumentRegistry();
            String mimeType = drr.parseRegistry(prdst);
            forwards = drr.getForwards();
            replyEmail = drr.getAuthorEmail();
            if (mimeType.indexOf("pdf") >= 0) {
                suffix = "pdf";
            } else {
                suffix = "xml";
            }

        } catch (Exception e) {
            e.printStackTrace();
            throw (e);
        }

        return forwards;
    }

    public RegistryResponseType getRepositoryProvideResponse(String messageId) throws Exception {
        RegistryResponseType rrt = null;
        try { // Call Web Service Operation

            String status = "urn:oasis:names:tc:ebxml-regrep:ResponseStatusType:Success";  // TODO initialize WS operation arguments here

            try {

                rrt = new RegistryResponseType();
                rrt.setStatus(status);


            } catch (Exception ex) {
                LOGGER.info("not sure what this ");
                ex.printStackTrace();
            }


        } catch (Exception ex) {
            ex.printStackTrace();
        }
        return rrt;
    }

    public String fowardMessage(int type, String body, List<String> forwards, String replyEmail, ProvideAndRegisterDocumentSetRequestType prdst, String messageId, String endpoint, String suffix, String meta) throws Exception {

        try {
            messageId = UUID.randomUUID().toString();
            boolean requestForward = true;

            if (requestForward && forwards.size() > 0) {

                forwardRepositoryRequest(type, messageId, forwards, prdst, replyEmail, body, suffix, meta);
            }

        } catch (Exception x) {
            x.printStackTrace();
            throw x;
        }
        return messageId;
    }

    private void forwardRepositoryRequest(int type, String messageId, List<String> provideEndpoints, ProvideAndRegisterDocumentSetRequestType prdst, String fromEmail, String body, String suffix, String meta) throws Exception {
        try {
            for (String reqEndpoint : provideEndpoints) {
                if (reqEndpoint.indexOf('@') > 0) {
                    byte[] docs = getDocs(prdst);
                    mailDocument(reqEndpoint, fromEmail, messageId, docs, suffix, meta.getBytes());
                } else if (reqEndpoint.equals("local")) {
                    //nothing
                } else {

                    String to = reqEndpoint;
                    to = to.replace("?wsdl", "");
                    Long threadId = new Long(Thread.currentThread().getId());
                    LOGGER.info("THREAD ID " + threadId);
                    ThreadData threadData = new ThreadData(threadId);
                    threadData.setTo(to);

                    List<Document> docs = prdst.getDocument();
                    Document oldDoc = docs.get(0);
                    docs.clear();
                    Document doc = new Document();
                    doc.setId(oldDoc.getId());

                    DataHandler dh = oldDoc.getValue();
                    ByteArrayOutputStream buffOS = new ByteArrayOutputStream();
                    dh.writeTo(buffOS);
                    byte[] buff = buffOS.toByteArray();

                    DataSource source = new ByteArrayDataSource(buff, "application/xml; charset=UTF-8");
                    DataHandler dhnew = new DataHandler(source);
                    doc.setValue(dhnew);

                    docs.add(doc);

                    LOGGER.info(" SENDING TO ENDPOINT " + to);
                    DocumentRepositoryService service = new DocumentRepositoryService();
                    service.setHandlerResolver(new RepositoryHandlerResolver());


                    DocumentRepositoryPortType port = service.getDocumentRepositoryPortSoap12(new MTOMFeature(true, 1));

                    BindingProvider bp = (BindingProvider) port;
                    SOAPBinding binding = (SOAPBinding) bp.getBinding();
                    binding.setMTOMEnabled(true);

                    bp.getRequestContext().put(BindingProvider.ENDPOINT_ADDRESS_PROPERTY, reqEndpoint);


                    RegistryResponseType rrt = port.documentRepositoryProvideAndRegisterDocumentSetB(prdst);
                    String test = rrt.getStatus();
                    if (test.indexOf("Failure") >= 0) {
                        throw new Exception("Failure Returned from XDR forward");
                    }


                }
            }


        } catch (Exception x) {
            x.printStackTrace();
        }
    }

    private void mailDocument(String email, String from, String messageId, byte[] message, String suffix, byte[] meta) throws Exception {
        SMTPMailClient smc = new SMTPMailClient();
        LOGGER.info("SENDING EMAIL TO " + email + " with message id " + messageId);
        List<String> recipients = new ArrayList<String>();
        recipients.add(email);

        smc.postMail(recipients, "data", messageId, "data attached", message, from, suffix, meta);
    }

    private byte[] getDocs(ProvideAndRegisterDocumentSetRequestType prdst) {
        List<Document> documents = prdst.getDocument();

        byte[] ret = null;
        try {
            for (ihe.iti.xds_b._2007.ProvideAndRegisterDocumentSetRequestType.Document doc : documents) {
                DataHandler dh = doc.getValue();
                ByteArrayOutputStream buffOS = new ByteArrayOutputStream();
                dh.writeTo(buffOS);
                ret = buffOS.toByteArray();

            }
        } catch (Exception x) {
            x.printStackTrace();
        }
        return ret;
    }

    protected void getHeaderData() {
        Long threadId = new Long(Thread.currentThread().getId());
        LOGGER.info("DTHREAD ID " + threadId);
        ThreadData threadData = new ThreadData(threadId);
        endpoint = threadData.getReplyAddress();
        messageId = threadData.getMessageId();
        to = threadData.getTo();
        thisHost = threadData.getThisHost();
        remoteHost = threadData.getRemoteHost();
        pid = threadData.getPid();
        action = threadData.getAction();
        from = threadData.getFrom();
    }

    protected void setHeaderData() {
        Long threadId = new Long(Thread.currentThread().getId());
        LOGGER.info("THREAD ID " + threadId);
        ThreadData threadData = new ThreadData(threadId);
        threadData.setTo(to);
        threadData.setMessageId(messageId);
        threadData.setRelatesTo(relatesTo);
        threadData.setAction(action);
        threadData.setThisHost(thisHost);
        threadData.setRemoteHost(remoteHost);
        threadData.setPid(pid);
        threadData.setFrom(from);
    }
}
