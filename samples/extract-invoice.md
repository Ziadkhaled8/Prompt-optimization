Extract invoice details from the text below.
Return a valid JSON object containing:
- invoice_number (string)
- vendor_name (string)
- total_amount (numeric)
- due_date (YYYY-MM-DD)
- line_items (list of objects with description and amount)

Do not include conversational preamble or markdown code fences.

Invoice text:
{{invoice_text}}
