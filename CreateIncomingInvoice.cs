using System;
using System.Collections.Generic;
using SAP.Middleware.Connector;

class Program
{
    static void Main()
    {
        // --- SAP destination (replace with your landscape credentials) ---
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

        // ---------- Controls you can tweak ----------
        int numInvoices = 1000;           // how many invoices to post
        string companyCode = "AUS";
        string currency    = "AUD";
        string docType     = "DR";        // Customer invoice
        string revenueGL   = "00041000400"; // pad to your chart length
        string profitCtr   = "100312au";
        string taxCode     = "ZZ";        // 0-tax

        // Allowed amounts: 500..1000 step 100
        int[] allowedAmounts = { 500, 600, 700, 800, 900, 1000 };

        var rng = new Random();

        // 1) Read existing AUS customers once, then pick randomly each invoice
        List<string> customers = FetchCustomersForCompany(repo, dest, companyCode);
        if (customers.Count == 0)
        {
            Console.WriteLine($"No customers found in company code {companyCode}. Aborting.");
            return;
        }

        for (int i = 1; i <= numInvoices; i++)
        {
            // Pick a random customer from KNB1 for AUS
            string customer = customers[rng.Next(customers.Count)];

            // Pick a random amount from the allowed steps (rounded to 100 by construction)
            decimal amount = allowedAmounts[rng.Next(allowedAmounts.Length)];

            // Unique external invoice ref for BKPF-XBLNR (<=16 chars)
            // Example: "AR0904-1502-0001"
            string reference = $"AR{DateTime.Now:MMdd-HHmm}-{i % 10000:D4}";

            Console.WriteLine($"Posting {i}/{numInvoices}: Customer {customer}, Amount {amount} {currency}, Ref {reference}");

            IRfcFunction post = repo.CreateFunction("BAPI_ACC_DOCUMENT_POST");

            // ===== Header =====
            IRfcStructure hdr = post.GetStructure("DOCUMENTHEADER");
            hdr.SetValue("COMP_CODE",  companyCode);
            hdr.SetValue("DOC_DATE",   DateTime.Today);
            hdr.SetValue("PSTNG_DATE", DateTime.Today);
            hdr.SetValue("DOC_TYPE",   docType);
            hdr.SetValue("CURRENCY",   currency);
            hdr.SetValue("HEADER_TXT", $"AUTO AR {i:D5}");
            hdr.SetValue("REFERENCE",  reference);

            // ===== AR line (Debit customer) =====
            IRfcTable ar = post.GetTable("ACCOUNTRECEIVABLE");
            ar.Append();
            ar.SetValue("ITEMNO_ACC", 1);
            ar.SetValue("CUSTOMER",   customer);
            ar.SetValue("ALLOC_NMBR", reference);
            ar.SetValue("ITEM_TEXT",  $"Customer invoice {i:D5}");

            // ===== Revenue line (Credit GL 41000400, Profit Center 100312au) =====
            IRfcTable gl = post.GetTable("ACCOUNTGL");
            gl.Append();
            gl.SetValue("ITEMNO_ACC", 2);
            gl.SetValue("GL_ACCOUNT", revenueGL);
            gl.SetValue("PROFIT_CTR", profitCtr);
            gl.SetValue("ITEM_TEXT",  $"Revenue {i:D5}");
            gl.SetValue("TAX_CODE",   taxCode); // ZZ = zero tax

            // ===== Amounts =====
            // Debit positive, credit negative
            IRfcTable amt = post.GetTable("CURRENCYAMOUNT");
            amt.Append();
            amt.SetValue("ITEMNO_ACC", 1);
            amt.SetValue("CURRENCY",   currency);
            amt.SetValue("AMT_DOCCUR", amount);      // Debit AR

            amt.Append();
            amt.SetValue("ITEMNO_ACC", 2);
            amt.SetValue("CURRENCY",   currency);
            amt.SetValue("AMT_DOCCUR", -amount);     // Credit revenue

            // --- Post ---
            post.Invoke(dest);

            // --- Check RETURN messages ---
            IRfcTable ret = post.GetTable("RETURN");
            bool hasError = false;
            for (int r = 0; r < ret.Count; r++)
            {
                ret.CurrentIndex = r;
                string type = ret.GetString("TYPE");
                string msg  = ret.GetString("MESSAGE");
                Console.WriteLine($"{type}: {msg}");
                if (type == "E" || type == "A") hasError = true;
            }

            if (hasError)
            {
                Console.WriteLine($"Invoice {i} failed. Skipping COMMIT.\n");
                continue;
            }

            // --- Commit & echo created key ---
            IRfcFunction commit = repo.CreateFunction("BAPI_TRANSACTION_COMMIT");
            commit.SetValue("WAIT", "X");
            commit.Invoke(dest);

            string objType = post.GetString("OBJ_TYPE");  // e.g., BKPFF
            string objKey  = post.GetString("OBJ_KEY");   // BKPF key (docno+year+bukrs)
            Console.WriteLine($"Committed. OBJ_TYPE={objType}, OBJ_KEY={objKey}\n");
        }

        Console.WriteLine("Finished posting customer invoices.");
    }

    // Pull KNB1 entries for given company code; return list of KUNNRs (keep leading zeros)
    static List<string> FetchCustomersForCompany(RfcRepository repo, RfcDestination dest, string companyCode)
    {
        var list = new List<string>();

        IRfcFunction read = repo.CreateFunction("RFC_READ_TABLE");
        read.SetValue("QUERY_TABLE", "KNB1");
        read.SetValue("DELIMITER",   "|");

        // Only need KUNNR column
        IRfcTable fields = read.GetTable("FIELDS");
        fields.Append();
        fields.SetValue("FIELDNAME", "KUNNR");

        // Restrict to company code
        IRfcTable options = read.GetTable("OPTIONS");
        options.Append();
        options.SetValue("TEXT", $"BUKRS = '{companyCode}'");

        // Optionally limit volume:
        // read.SetValue("ROWCOUNT", 5000);

        read.Invoke(dest);

        IRfcTable data = read.GetTable("DATA");
        for (int i = 0; i < data.Count; i++)
        {
            data.CurrentIndex = i;
            string wa = data.GetString("WA");      // e.g., "0000123456"
            string kunnr = wa.Split('|')[0].Trim();// keep leading zeros
            if (!string.IsNullOrEmpty(kunnr))
                list.Add(kunnr);
        }

        return list;
    }
}
