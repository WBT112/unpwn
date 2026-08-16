# Account classification catalog — third-party notices

The generated `account-classification-catalog.tsv` is repository-controlled runtime data.
Its recovery categories are an unpwn policy decision; the upstream projects provide domain/service grouping data and do not make unpwn's security-priority claims.

## v2fly/domain-list-community

- Source: `v2fly/domain-list-community`
- Pinned source commit: `6f8a5b43db087ae27decef85d80229850bbd40b1`
- License: MIT
- Used for: service/owner domain groupings selected by the category roots recorded in `account-classification-catalog.meta.json`

Copyright (c) 2018-2019 V2Ray

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## email-providers

- Source package: `email-providers`
- Pinned npm package version: `2.24.0`
- Author metadata: Jannis R
- License declared by the package: ISC
- Used for: the package's `common.json` mailbox-domain list, followed by unpwn's explicit mailbox-family grouping

Copyright (c) Jannis R

Permission to use, copy, modify, and/or distribute this software for any purpose with or without fee is hereby granted, provided that the above copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.

## Updating

Run from the repository root on a trusted development machine with Git, Python 3 and npm available:

```shell
python3 eng/update-account-classification-catalog.py
```

The updater fetches only the pinned inputs defined in the script. Review changes to the generator, source pins, category roots, canonical provider records, aliases, counts and this notice before merging regenerated data. Runtime unpwn builds do not run this updater and do not need network access for account classification.
