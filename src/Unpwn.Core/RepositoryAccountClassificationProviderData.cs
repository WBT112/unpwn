namespace Unpwn.Core;

internal static class RepositoryAccountClassificationProviderData
{
    public static IReadOnlyList<AccountClassificationProviderRecord> CreateExpandedRecords(
        string provenanceId) =>
    [
        .. CreateEmailRecords(provenanceId),
        .. CreateFinancialRecords(provenanceId),
        .. CreatePasswordAndIdentityRecords(provenanceId),
        .. CreateCloudAndDeveloperRecords(provenanceId),
        .. CreateCommerceAndCommunicationRecords(provenanceId),
        .. CreateGovernmentHealthAndInsuranceRecords(provenanceId),
        .. CreateLowerImpactConsumerRecords(provenanceId),
        .. RepositoryAccountClassificationAdditionalProviderData.CreateRecords(provenanceId),
    ];

    private static IReadOnlyList<AccountClassificationProviderRecord> CreateEmailRecords(
        string provenanceId) =>
    [
        Email("email-posteo", "Posteo", ["posteo.de"], provenanceId, "posteo"),
        Email("email-hey", "HEY", ["hey.com"], provenanceId, "hey"),
        Email("email-hushmail", "Hushmail", ["hushmail.com"], provenanceId, "hushmail"),
        Email("email-mailfence", "Mailfence", ["mailfence.com"], provenanceId, "mailfence"),
        Email("email-startmail", "StartMail", ["startmail.com"], provenanceId, "startmail"),
        Email("email-runbox", "Runbox", ["runbox.com"], provenanceId, "runbox"),
        Email("email-infomaniak", "Infomaniak Mail", ["infomaniak.com", "infomaniak.ch"], provenanceId,
            "infomaniakmail"),
        Email("email-laposte", "Laposte.net", ["laposte.net"], provenanceId, "lapostemail"),
        Email("email-sfr", "SFR Mail", ["sfr.fr", "neuf.fr"], provenanceId, "sfrmail"),
        Email("email-free-fr", "Free Mail", ["free.fr"], provenanceId, "freemailfr"),
        Email("email-tiscali-it", "Tiscali Mail", ["tiscali.it"], provenanceId, "tiscalimail"),
        Email("email-wp-poland", "WP / o2 Poczta", ["wp.pl", "o2.pl"], provenanceId, "wppoczta", "o2poczta"),
        Email("email-interia", "Interia Poczta", ["interia.pl"], provenanceId, "interiapoczta"),
        Email("email-onet", "Onet Poczta", ["onet.pl"], provenanceId, "onetpoczta"),
        Email("email-ukrnet", "Ukr.net Mail", ["ukr.net"], provenanceId, "ukrnetmail"),
        Email("email-rambler", "Rambler Mail", ["rambler.ru"], provenanceId, "ramblermail"),
        Email("email-inbox-lv", "Inbox.lv", ["inbox.lv"], provenanceId, "inboxlv"),
        Email("email-mailo", "Mailo", ["mailo.com"], provenanceId, "mailo"),
        Email("email-disroot", "Disroot Mail", ["disroot.org"], provenanceId, "disrootmail"),
        Email("email-uol", "UOL / BOL Mail", ["uol.com.br", "bol.com.br"], provenanceId, "uolmail", "bolmail"),
        Email("email-bluewin", "Swisscom Bluewin Mail", ["bluewin.ch"], provenanceId, "bluewin"),
        Email("email-bt", "BT Mail", ["btinternet.com", "btopenworld.com"], provenanceId, "btmail"),
        Email("email-att", "AT&T Mail", ["att.net", "sbcglobal.net", "bellsouth.net", "pacbell.net"],
            provenanceId, "attmail"),
        Email("email-comcast", "Comcast Mail", ["comcast.net"], provenanceId, "comcastmail"),
        Email("email-earthlink", "EarthLink Mail", ["earthlink.net", "mindspring.com"], provenanceId,
            "earthlinkmail"),
        Email("email-cox", "Cox Mail", ["cox.net"], provenanceId, "coxmail"),
        Email("email-virgin-media", "Virgin Media Mail",
            ["virginmedia.com", "ntlworld.com", "blueyonder.co.uk"], provenanceId, "virginmediamail"),
        Email("email-sky", "Sky Yahoo Mail", ["sky.com"], provenanceId, "skymail"),
        Email("email-rediff", "Rediffmail", ["rediffmail.com"], provenanceId, "rediffmail"),
    ];

    private static IReadOnlyList<AccountClassificationProviderRecord> CreateFinancialRecords(
        string provenanceId) =>
    [
        Financial("critical-dkb", "DKB", ["dkb.de"], provenanceId, "dkb"),
        Financial("critical-ing-de", "ING Germany", ["ing.de", "ing-diba.de"], provenanceId, "ingde", "ingdiba"),
        Financial("critical-comdirect", "Comdirect", ["comdirect.de"], provenanceId, "comdirect"),
        Financial("critical-consorsbank", "Consorsbank", ["consorsbank.de"], provenanceId, "consorsbank"),
        Financial("critical-postbank", "Postbank", ["postbank.de"], provenanceId, "postbank"),
        Financial("critical-apobank", "Deutsche Apotheker- und Ärztebank", ["apobank.de"], provenanceId,
            "apobank"),
        Financial("critical-hypovereinsbank", "HypoVereinsbank", ["hypovereinsbank.de"], provenanceId,
            "hypovereinsbank", "hvb"),
        Financial("critical-santander-de", "Santander Germany", ["santander.de"], provenanceId, "santanderde"),
        Financial("critical-targobank", "TARGOBANK", ["targobank.de"], provenanceId, "targobank"),
        Financial("critical-trade-republic", "Trade Republic", ["traderepublic.com"], provenanceId,
            "traderepublic"),
        Financial("critical-scalable-capital", "Scalable Capital", ["scalable.capital"], provenanceId,
            "scalablecapital"),
        Financial("critical-bawag", "BAWAG", ["bawag.at"], provenanceId, "bawag"),
        Financial("critical-erste-at", "Erste Bank Austria", ["erstebank.at"], provenanceId, "erstebankat"),
        Financial("critical-raiffeisen-ch", "Raiffeisen Switzerland", ["raiffeisen.ch"], provenanceId,
            "raiffeisench"),
        Financial("critical-ubs", "UBS", ["ubs.com"], provenanceId, "ubs"),
        Financial("critical-credit-suisse", "Credit Suisse", ["credit-suisse.com"], provenanceId,
            "creditsuisse"),
        Financial("critical-postfinance", "PostFinance", ["postfinance.ch"], provenanceId, "postfinance"),
        Financial("critical-abn-amro", "ABN AMRO", ["abnamro.nl"], provenanceId, "abnamro"),
        Financial("critical-rabobank-nl", "Rabobank Netherlands", ["rabobank.nl"], provenanceId, "rabobanknl"),
        Financial("critical-belfius", "Belfius", ["belfius.be"], provenanceId, "belfius"),
        Financial("critical-kbc", "KBC", ["kbc.be"], provenanceId, "kbc"),
        Financial("critical-bnp-paribas", "BNP Paribas", ["bnpparibas.com", "bnpparibas.fr"], provenanceId,
            "bnpparibas"),
        Financial("critical-credit-agricole", "Crédit Agricole", ["credit-agricole.fr"], provenanceId,
            "creditagricole"),
        Financial("critical-societe-generale", "Société Générale", ["societegenerale.fr"], provenanceId,
            "societegenerale"),
        Financial("critical-boursobank", "BoursoBank", ["boursobank.com"], provenanceId, "boursobank"),
        Financial("critical-barclays", "Barclays", ["barclays.co.uk"], provenanceId, "barclays"),
        Financial("critical-lloyds", "Lloyds Bank", ["lloydsbank.com"], provenanceId, "lloydsbank"),
        Financial("critical-natwest", "NatWest", ["natwest.com"], provenanceId, "natwest"),
        Financial("critical-monzo", "Monzo", ["monzo.com"], provenanceId, "monzo"),
        Financial("critical-starling", "Starling Bank", ["starlingbank.com"], provenanceId, "starlingbank"),
        Financial("critical-citi", "Citi", ["citi.com", "citibank.com"], provenanceId, "citi", "citibank"),
        Financial("critical-wells-fargo", "Wells Fargo", ["wellsfargo.com"], provenanceId, "wellsfargo"),
        Financial("critical-capital-one", "Capital One", ["capitalone.com"], provenanceId, "capitalone"),
        Financial("critical-american-express", "American Express", ["americanexpress.com"], provenanceId,
            "americanexpress", "amex"),
        Financial("critical-discover", "Discover", ["discover.com"], provenanceId, "discover"),
        Financial("critical-schwab", "Charles Schwab", ["schwab.com"], provenanceId, "charlesschwab", "schwab"),
        Financial("critical-vanguard", "Vanguard", ["vanguard.com"], provenanceId, "vanguard"),
        Financial("critical-rbc", "RBC Royal Bank", ["rbcroyalbank.com"], provenanceId, "rbcroyalbank"),
        Financial("critical-td", "TD Bank", ["td.com"], provenanceId, "tdbank"),
        Financial("critical-scotiabank", "Scotiabank", ["scotiabank.com"], provenanceId, "scotiabank"),
        Financial("critical-bmo", "BMO", ["bmo.com"], provenanceId, "bmo"),
        Financial("critical-cibc", "CIBC", ["cibc.com"], provenanceId, "cibc"),
        Financial("critical-anz", "ANZ", ["anz.com"], provenanceId, "anz"),
        Financial("critical-commonwealth-bank", "Commonwealth Bank", ["commbank.com.au"], provenanceId,
            "commonwealthbank", "commbank"),
        Financial("critical-westpac", "Westpac", ["westpac.com.au"], provenanceId, "westpac"),
        Financial("critical-nab", "National Australia Bank", ["nab.com.au"], provenanceId, "nab"),
        Financial("critical-mastercard", "Mastercard", ["mastercard.com"], provenanceId, "mastercard"),
        Financial("critical-adyen", "Adyen", ["adyen.com"], provenanceId, "adyen"),
        Financial("critical-sumup", "SumUp", ["sumup.com"], provenanceId, "sumup"),
        Financial("critical-square", "Square", ["squareup.com"], provenanceId, "square"),
        Financial("critical-coinbase", "Coinbase", ["coinbase.com"], provenanceId, "coinbase"),
        Financial("critical-kraken", "Kraken", ["kraken.com"], provenanceId, "kraken"),
        Financial("critical-binance", "Binance", ["binance.com"], provenanceId, "binance"),
        Financial("critical-bitpanda", "Bitpanda", ["bitpanda.com"], provenanceId, "bitpanda"),
        Financial("critical-venmo", "Venmo", ["venmo.com"], provenanceId, "venmo"),
        Financial("critical-cash-app", "Cash App", ["cash.app"], provenanceId, "cashapp"),
        Financial("critical-nordnet", "Nordnet", ["nordnet.se"], provenanceId, "nordnet"),
        Financial("critical-degiro", "DEGIRO", ["degiro.eu"], provenanceId, "degiro"),
        Financial("critical-flatex", "flatex", ["flatex.de"], provenanceId, "flatex"),
        Financial("critical-etoro", "eToro", ["etoro.com"], provenanceId, "etoro"),
        Financial("critical-robinhood", "Robinhood", ["robinhood.com"], provenanceId, "robinhood"),
    ];

    private static IReadOnlyList<AccountClassificationProviderRecord> CreatePasswordAndIdentityRecords(
        string provenanceId) =>
    [
        PasswordManager("critical-dashlane", "Dashlane", ["dashlane.com"], provenanceId, "dashlane"),
        PasswordManager("critical-keeper", "Keeper", ["keepersecurity.com"], provenanceId, "keeper"),
        PasswordManager("critical-nordpass", "Nord Account (NordPass / NordVPN)",
            ["nordpass.com", "nordaccount.com", "nordvpn.com"], provenanceId,
            "nordpass", "nordaccount", "nordvpn"),
        PasswordManager("critical-enpass", "Enpass", ["enpass.io"], provenanceId, "enpass"),
        PasswordManager("critical-roboform", "RoboForm", ["roboform.com"], provenanceId, "roboform"),
        PasswordManager("critical-logmeonce", "LogMeOnce", ["logmeonce.com"], provenanceId, "logmeonce"),
        Identity("critical-ping-identity", "Ping Identity", ["pingidentity.com"], provenanceId, "pingidentity"),
        Identity("critical-duo", "Cisco Duo", ["duo.com"], provenanceId, "duo"),
    ];

    private static IReadOnlyList<AccountClassificationProviderRecord> CreateCloudAndDeveloperRecords(
        string provenanceId) =>
    [
        Cloud("critical-gitlab", "GitLab", ["gitlab.com"], provenanceId, "gitlab"),
        Cloud("critical-atlassian", "Atlassian / Bitbucket / Trello",
            ["atlassian.com", "bitbucket.org", "trello.com"], provenanceId,
            "atlassian", "bitbucket", "trello"),
        Cloud("critical-cloudflare", "Cloudflare", ["cloudflare.com"], provenanceId, "cloudflare"),
        Cloud("critical-digitalocean", "DigitalOcean", ["digitalocean.com"], provenanceId, "digitalocean"),
        Cloud("critical-heroku", "Heroku", ["heroku.com"], provenanceId, "heroku"),
        Cloud("critical-vercel", "Vercel", ["vercel.com"], provenanceId, "vercel"),
        Cloud("critical-netlify", "Netlify", ["netlify.com"], provenanceId, "netlify"),
        Cloud("critical-docker", "Docker", ["docker.com"], provenanceId, "docker"),
        Cloud("critical-npm", "npm", ["npmjs.com"], provenanceId, "npm", "npmjs"),
        Cloud("critical-jetbrains", "JetBrains", ["jetbrains.com"], provenanceId, "jetbrains"),
        Cloud("critical-oracle", "Oracle", ["oracle.com"], provenanceId, "oracle"),
        Cloud("critical-ibm", "IBM", ["ibm.com"], provenanceId, "ibm"),
        Cloud("critical-salesforce", "Salesforce", ["salesforce.com"], provenanceId, "salesforce"),
        Cloud("critical-adobe", "Adobe", ["adobe.com"], provenanceId, "adobe"),
        Cloud("critical-twilio", "Twilio", ["twilio.com"], provenanceId, "twilio"),
    ];

    private static IReadOnlyList<AccountClassificationProviderRecord> CreateCommerceAndCommunicationRecords(
        string provenanceId) =>
    [
        Commerce("critical-zalando", "Zalando", ["zalando.de"], provenanceId, "zalando"),
        Commerce("critical-otto", "OTTO", ["otto.de"], provenanceId, "otto"),
        Commerce("critical-kleinanzeigen", "Kleinanzeigen", ["kleinanzeigen.de"], provenanceId, "kleinanzeigen"),
        Commerce("critical-vinted", "Vinted", ["vinted.com", "vinted.de"], provenanceId, "vinted"),
        Commerce("critical-shopify", "Shopify", ["shopify.com"], provenanceId, "shopify"),
        Commerce("critical-alibaba", "Alibaba", ["alibaba.com"], provenanceId, "alibaba"),
        Commerce("critical-aliexpress", "AliExpress", ["aliexpress.com"], provenanceId, "aliexpress"),
        Commerce("critical-temu", "Temu", ["temu.com"], provenanceId, "temu"),
        Commerce("critical-booking", "Booking.com", ["booking.com"], provenanceId, "bookingcom"),
        Commerce("critical-airbnb", "Airbnb", ["airbnb.com"], provenanceId, "airbnb"),
        Commerce("critical-uber", "Uber", ["uber.com"], provenanceId, "uber"),
        Commerce("critical-lieferando", "Lieferando", ["lieferando.de"], provenanceId, "lieferando"),
        Commerce("critical-ikea", "IKEA", ["ikea.com"], provenanceId, "ikea"),
        Commerce("critical-mediamarkt", "MediaMarkt", ["mediamarkt.de"], provenanceId, "mediamarkt"),
        Commerce("critical-saturn", "Saturn", ["saturn.de"], provenanceId, "saturn"),
        Commerce("critical-bol", "bol.com", ["bol.com"], provenanceId, "bolcom"),
        Commerce("critical-allegro", "Allegro", ["allegro.pl"], provenanceId, "allegro"),
        Communications("critical-whatsapp", "WhatsApp", ["whatsapp.com"], provenanceId, "whatsapp"),
        Communications("critical-telegram", "Telegram", ["telegram.org"], provenanceId, "telegram"),
        Communications("critical-signal", "Signal", ["signal.org"], provenanceId, "signal"),
        Communications("critical-slack", "Slack", ["slack.com"], provenanceId, "slack"),
        Communications("critical-zoom", "Zoom", ["zoom.us"], provenanceId, "zoom"),
        Communications("critical-tiktok", "TikTok", ["tiktok.com"], provenanceId, "tiktok"),
        Communications("critical-snapchat", "Snapchat", ["snapchat.com"], provenanceId, "snapchat"),
        Communications("critical-bluesky", "Bluesky", ["bsky.app"], provenanceId, "bluesky", "bsky"),
        Communications("critical-threema", "Threema", ["threema.ch"], provenanceId, "threema"),
    ];

    private static IReadOnlyList<AccountClassificationProviderRecord> CreateGovernmentHealthAndInsuranceRecords(
        string provenanceId) =>
    [
        SensitiveIdentity("critical-bundid", "BundID", ["id.bund.de"], provenanceId, "bundid"),
        SensitiveIdentity("critical-elster", "ELSTER", ["elster.de"], provenanceId, "elster"),
        SensitiveIdentity("critical-id-austria", "ID Austria", ["id-austria.gv.at"], provenanceId, "idaustria"),
        SensitiveIdentity("critical-govuk-one-login", "GOV.UK One Login", ["account.gov.uk"], provenanceId,
            "govukonelogin"),
        SensitiveIdentity("critical-nhs-account", "NHS Account", ["account.nhs.uk"], provenanceId, "nhsaccount"),
        SensitiveIdentity("critical-doctolib", "Doctolib", ["doctolib.de", "doctolib.fr"], provenanceId,
            "doctolib"),
        SensitiveIdentity("critical-tk", "Techniker Krankenkasse", ["tk.de"], provenanceId, "tk", "techniker"),
        SensitiveIdentity("critical-aok", "AOK", ["aok.de"], provenanceId, "aok"),
        SensitiveIdentity("critical-barmer", "BARMER", ["barmer.de"], provenanceId, "barmer"),
        SensitiveIdentity("critical-ameli", "ameli", ["ameli.fr"], provenanceId, "ameli"),
        SensitiveIdentity("critical-impots-gouv", "impots.gouv.fr", ["impots.gouv.fr"], provenanceId, "impotsgouv"),
        SensitiveIdentity("critical-allianz-de", "Allianz Germany", ["allianz.de"], provenanceId, "allianzde"),
        SensitiveIdentity("critical-axa-de", "AXA Germany", ["axa.de"], provenanceId, "axade"),
        SensitiveIdentity("critical-huk", "HUK-COBURG", ["huk.de"], provenanceId, "huk", "hukcoburg"),
    ];

    private static IReadOnlyList<AccountClassificationProviderRecord> CreateLowerImpactConsumerRecords(
        string provenanceId) =>
    [
        LowerImpact("noncritical-disneyplus", "Disney+", ["disneyplus.com"], provenanceId, "disneyplus"),
        LowerImpact("noncritical-max", "Max", ["max.com"], provenanceId, "maxstreaming"),
        LowerImpact("noncritical-hulu", "Hulu", ["hulu.com"], provenanceId, "hulu"),
        LowerImpact("noncritical-dazn", "DAZN", ["dazn.com"], provenanceId, "dazn"),
        LowerImpact("noncritical-soundcloud", "SoundCloud", ["soundcloud.com"], provenanceId, "soundcloud"),
        LowerImpact("noncritical-deezer", "Deezer", ["deezer.com"], provenanceId, "deezer"),
        LowerImpact("noncritical-nytimes", "The New York Times", ["nytimes.com"], provenanceId, "nytimes"),
        LowerImpact("noncritical-guardian", "The Guardian", ["theguardian.com"], provenanceId, "theguardian"),
        LowerImpact("noncritical-spiegel", "DER SPIEGEL", ["spiegel.de"], provenanceId, "spiegel"),
        LowerImpact("noncritical-zeit", "DIE ZEIT", ["zeit.de"], provenanceId, "zeit"),
        LowerImpact("noncritical-faz", "Frankfurter Allgemeine", ["faz.net"], provenanceId, "faz"),
        LowerImpact("noncritical-sueddeutsche", "Süddeutsche Zeitung", ["sueddeutsche.de"], provenanceId,
            "sueddeutsche"),
        LowerImpact("noncritical-bbc", "BBC", ["bbc.com"], provenanceId, "bbc"),
        LowerImpact("noncritical-strava", "Strava", ["strava.com"], provenanceId, "strava"),
        LowerImpact("noncritical-tripadvisor", "Tripadvisor", ["tripadvisor.com"], provenanceId, "tripadvisor"),
        LowerImpact("noncritical-letterboxd", "Letterboxd", ["letterboxd.com"], provenanceId, "letterboxd"),
    ];

    private static AccountClassificationProviderRecord Email(
        string id, string name, string[] domains, string provenanceId, params string[] aliases) =>
        Record(id, name, AccountRecoveryCategory.Email, domains, aliases, provenanceId,
            "Provider-owned consumer mailbox account family; mailbox access can reset other accounts. " +
            "Reviewed from the historical webmail candidates against current provider-owned domains.");

    private static AccountClassificationProviderRecord Financial(
        string id, string name, string[] domains, string provenanceId, params string[] aliases) =>
        Record(id, name, AccountRecoveryCategory.Critical, domains, aliases, provenanceId,
            "Provider-owned banking, payment, investment, or crypto account family; takeover can affect money and identity. " +
            "Reviewed against provider domains and applicable public financial-register evidence.");

    private static AccountClassificationProviderRecord PasswordManager(
        string id, string name, string[] domains, string provenanceId, params string[] aliases) =>
        Record(id, name, AccountRecoveryCategory.Critical, domains, aliases, provenanceId,
            "Provider-owned password-manager vault account family; takeover can expose credentials for other accounts.");

    private static AccountClassificationProviderRecord Identity(
        string id, string name, string[] domains, string provenanceId, params string[] aliases) =>
        Record(id, name, AccountRecoveryCategory.Critical, domains, aliases, provenanceId,
            "Provider-owned identity account family; takeover can grant access to connected services.");

    private static AccountClassificationProviderRecord Cloud(
        string id, string name, string[] domains, string provenanceId, params string[] aliases) =>
        Record(id, name, AccountRecoveryCategory.Critical, domains, aliases, provenanceId,
            "Provider-owned cloud or developer account family; takeover can affect hosted data, code, infrastructure, or customer systems.");

    private static AccountClassificationProviderRecord Commerce(
        string id, string name, string[] domains, string provenanceId, params string[] aliases) =>
        Record(id, name, AccountRecoveryCategory.Critical, domains, aliases, provenanceId,
            "Provider-owned commerce or marketplace account family with payment, order, address, seller, or transaction impact.");

    private static AccountClassificationProviderRecord Communications(
        string id, string name, string[] domains, string provenanceId, params string[] aliases) =>
        Record(id, name, AccountRecoveryCategory.Critical, domains, aliases, provenanceId,
            "Provider-owned communications or social-identity account family; takeover enables impersonation or message access.");

    private static AccountClassificationProviderRecord SensitiveIdentity(
        string id, string name, string[] domains, string provenanceId, params string[] aliases) =>
        Record(id, name, AccountRecoveryCategory.Critical, domains, aliases, provenanceId,
            "Provider-owned government, health, or insurance account family containing identity, official, medical, or coverage data.");

    private static AccountClassificationProviderRecord LowerImpact(
        string id, string name, string[] domains, string provenanceId, params string[] aliases) =>
        Record(id, name, AccountRecoveryCategory.NonCritical, domains, aliases, provenanceId,
            "Provider-owned consumer media, gaming, reading, or leisure account family where delayed recovery is normally defensible.");

    private static AccountClassificationProviderRecord Record(
        string id,
        string name,
        AccountRecoveryCategory category,
        string[] domains,
        string[] aliases,
        string provenanceId,
        string reviewBasis) =>
        new(
            id,
            name,
            category,
            Array.AsReadOnly(domains),
            Array.AsReadOnly(aliases),
            provenanceId,
            reviewBasis);
}
