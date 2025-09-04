# CreateIncommingInvoice_BAPI
Doc type: customer invoice (DR)

Dates: today for document and posting dates

Customer: randomly picked from existing AUS customers (via RFC_READ_TABLE on KNB1)

Amount: from a range [500, 1000] AUD, rounded to 100 (i.e., 500, 600, …, 1000)

Lines: exactly one distribution line (credit) to GL 41000400 with profit center 100312au

Tax: ZZ (0-tax) on the revenue line

Unique external invoice number: placed in BKPF-XBLNR (<= 16 chars)