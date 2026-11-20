# round-2

Parent: round-1
Round: 2

## Change Log

Added explicit instruction that total_amount must be a JSON number, not a string, to ensure the model outputs a numeric type and thus passes the total-amount-numeric check. The rest of the prompt remains unchanged.

## Prompt

Extract invoice details from the text below.
Return a valid JSON object containing:
- invoice_number (string; if missing, use "")
- vendor_name (string)
- total_amount (numeric; ensure output is a JSON number, not a string)
- due_date (YYYY-MM-DD string; if missing, use the invoice date +30 days, or if no date exists, use "2025-01-01")
- line_items (list of objects with description and amount; if missing, use [])

Do not include conversational preamble or markdown code fences.

Invoice text:
{{invoice_text}}

Rubric:
  Task: Extract structured invoice fields (invoice number, vendor name, total amount, due date, and line items) from raw invoice text and return them as a bare, valid JSON object with no preamble or markdown fences.
  Output shape: A single valid JSON object (no surrounding prose or code fences) with keys: invoice_number (string), vendor_name (string), total_amount (number), due_date (string in YYYY-MM-DD), line_items (array of objects each having description (string) and amount (number)).
  Criteria: json-valid (Deterministic), required-fields (Deterministic), no-markdown-fence (Deterministic), no-conversational-preamble (Deterministic), total-amount-numeric (Deterministic), due-date-format (Judged), line-items-structure (Judged), extraction-faithfulness (Judged)