using System;
using SAP.Middleware.Connector;

class Program
{
    static void Main()
    {
        // --- connect (replace with your own destination config) ---
        var parms = new RfcConfigParameters
        {
            { RfcConfigParameters.AppServerHost, "sap-app-host" },
            { RfcConfigParameters.SystemNumber, "00" },
            { RfcConfigParameters.SystemID,     "ECC" },
            { RfcConfigParameters.User,         "MYUSER" },
            { RfcConfigParameters.Password,     "MYPASS" },
            { RfcConfigParameters.Client,       "100" },
            { RfcConfigParameters.Language,     "EN" }
        };
        var dest = RfcDestinationManager.GetDestination(parms);
        var repo = dest.Repository;

        // --- BAPI handle ---
        IRfcFunction bapi = repo.CreateFunction("BAPI_INCOMINGINVOICE_CREATE");

        // ================= HEADER =================
        IRfcStructure header = bapi.GetStructure("HEADERDATA");
        header.SetValue("INVOICE_IND", "X");              // Invoice (not credit memo)
        header.SetValue("DOC_TYPE",    "RE");             // MM Invoice doc type
        header.SetValue("DOC_DATE",    DateTime.Today);   // Invoice date
        header.SetValue("PSTNG_DATE",  DateTime.Today);   // Posting date
        header.SetValue("COMP_CODE",   "AUS");            // Company code
        header.SetValue("CURRENCY",    "AUD");            // Currency
        header.SetValue("GROSS_AMOUNT", 1000m);           // Total amount
        header.SetValue("VENDOR",      "1100688617");     // Vendor account (FK)
        header.SetValue("REF_DOC_NO",  "INV-NONPO-0001"); // Your external reference

        // Optional: payment terms / baseline date if needed
        // header.SetValue("PMNTTRMS", "0001");
        // header.SetValue("BLINE_DATE", DateTime.Today);

        // =============== GLACCOUNTDATA (items) ===============
        // Non-PO invoices are distributed via GLACCOUNTDATA (and ACCOUNTINGDATA for CO).
        IRfcTable gl = bapi.GetTable("GLACCOUNTDATA");
        gl.Append();
        gl.SetValue("INVOICE_DOC_ITEM", 1);          // Item 1
        gl.SetValue("GL_ACCOUNT", "00041000400");    // Zero-padded to 10/12 chars as per your system
        gl.SetValue("ITEM_AMOUNT", 1000m);           // Amount for this line
        gl.SetValue("TAX_CODE", "ZZ");               // Zero/No-tax code as configured in your system
        gl.SetValue("ITEM_TEXT", "Non-PO expense");  // Line text (optional)

        // =============== ACCOUNTINGDATA (CO assignment) ===============
        IRfcTable acc = bapi.GetTable("ACCOUNTINGDATA");
        acc.Append();
        acc.SetValue("INVOICE_DOC_ITEM", 1);         // Link to the GL line above
        acc.SetValue("PROFIT_CTR", "100312au");      // Profit center (exact key in your system)
        // If your config requires Cost Center, WBS, etc., set them here too:
        // acc.SetValue("COSTCENTER", "10001000");

        // =============== (No TAXDATA needed) ===============
        // For a no-tax posting, don’t pass TAXDATA; the ZZ tax code should be zero-rated in config.

        // --- Invoke ---
        bapi.Invoke(dest);

        // --- Check RETURN messages ---
        IRfcTable ret = bapi.GetTable("RETURN");
        bool hasError = false;
        for (int i = 0; i < ret.Count; i++)
        {
            ret.CurrentIndex = i;
            string type = ret.GetString("TYPE"); // S,W,E,A
            string msg  = ret.GetString("MESSAGE");
            Console.WriteLine($"{type}: {msg}");
            if (type == "E" || type == "A") hasError = true;
        }
        if (hasError)
        {
            Console.WriteLine("Invoice NOT posted due to errors.");
            return;
        }

        // --- Read created doc & commit ---
        string invDoc = bapi.GetString("INVOICEDOCNUMBER");
        string gjahr  = bapi.GetString("FISCALYEAR");
        Console.WriteLine($"Created Invoice Doc: {invDoc} / Year: {gjahr}");

        IRfcFunction commit = repo.CreateFunction("BAPI_TRANSACTION_COMMIT");
        commit.SetValue("WAIT", "X");
        commit.Invoke(dest);

        Console.WriteLine("Committed.");
    }
}
