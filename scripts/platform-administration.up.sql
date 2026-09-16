START TRANSACTION;
CREATE OR REPLACE FUNCTION queueflow_normalize_email(value text) RETURNS text
LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE AS $normalize$
    SELECT translate(btrim(value, U&'\0009\000a\000b\000c\000d\0020\0085\00a0\1680\2000\2001\2002\2003\2004\2005\2006\2007\2008\2009\200a\2028\2029\202f\205f\3000'), 'ABCDEFGHIJKLMNOPQRSTUVWXYZÀÁÂÃÄÅÆÇÈÉÊËÌÍÎÏÐÑÒÓÔÕÖØÙÚÛÜÝÞĀĂĄĆĈĊČĎĐĒĔĖĘĚĜĞĠĢĤĦĨĪĬĮĲĴĶĹĻĽĿŁŃŅŇŊŌŎŐŒŔŖŘŚŜŞŠŢŤŦŨŪŬŮŰŲŴŶŸŹŻŽƁƂƄƆƇƉƊƋƎƏƐƑƓƔƖƗƘƜƝƟƠƢƤƦƧƩƬƮƯƱƲƳƵƷƸƼǄǅǇǈǊǋǍǏǑǓǕǗǙǛǞǠǢǤǦǨǪǬǮǱǲǴǶǷǸǺǼǾȀȂȄȆȈȊȌȎȐȒȔȖȘȚȜȞȠȢȤȦȨȪȬȮȰȲȺȻȽȾɁɃɄɅɆɈɊɌɎͰͲͶͿΆΈΉΊΌΎΏΑΒΓΔΕΖΗΘΙΚΛΜΝΞΟΠΡΣΤΥΦΧΨΩΪΫϏϘϚϜϞϠϢϤϦϨϪϬϮϴϷϹϺϽϾϿЀЁЂЃЄЅІЇЈЉЊЋЌЍЎЏАБВГДЕЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯѠѢѤѦѨѪѬѮѰѲѴѶѸѺѼѾҀҊҌҎҐҒҔҖҘҚҜҞҠҢҤҦҨҪҬҮҰҲҴҶҸҺҼҾӀӁӃӅӇӉӋӍӐӒӔӖӘӚӜӞӠӢӤӦӨӪӬӮӰӲӴӶӸӺӼӾԀԂԄԆԈԊԌԎԐԒԔԖԘԚԜԞԠԢԤԦԨԪԬԮԱԲԳԴԵԶԷԸԹԺԻԼԽԾԿՀՁՂՃՄՅՆՇՈՉՊՋՌՍՎՏՐՑՒՓՔՕՖႠႡႢႣႤႥႦႧႨႩႪႫႬႭႮႯႰႱႲႳႴႵႶႷႸႹႺႻႼႽႾႿჀჁჂჃჄჅჇჍᎠᎡᎢᎣᎤᎥᎦᎧᎨᎩᎪᎫᎬᎭᎮᎯᎰᎱᎲᎳᎴᎵᎶᎷᎸᎹᎺᎻᎼᎽᎾᎿᏀᏁᏂᏃᏄᏅᏆᏇᏈᏉᏊᏋᏌᏍᏎᏏᏐᏑᏒᏓᏔᏕᏖᏗᏘᏙᏚᏛᏜᏝᏞᏟᏠᏡᏢᏣᏤᏥᏦᏧᏨᏩᏪᏫᏬᏭᏮᏯᏰᏱᏲᏳᏴᏵᲐᲑᲒᲓᲔᲕᲖᲗᲘᲙᲚᲛᲜᲝᲞᲟᲠᲡᲢᲣᲤᲥᲦᲧᲨᲩᲪᲫᲬᲭᲮᲯᲰᲱᲲᲳᲴᲵᲶᲷᲸᲹᲺᲽᲾᲿḀḂḄḆḈḊḌḎḐḒḔḖḘḚḜḞḠḢḤḦḨḪḬḮḰḲḴḶḸḺḼḾṀṂṄṆṈṊṌṎṐṒṔṖṘṚṜṞṠṢṤṦṨṪṬṮṰṲṴṶṸṺṼṾẀẂẄẆẈẊẌẎẐẒẔẞẠẢẤẦẨẪẬẮẰẲẴẶẸẺẼẾỀỂỄỆỈỊỌỎỐỒỔỖỘỚỜỞỠỢỤỦỨỪỬỮỰỲỴỶỸỺỼỾἈἉἊἋἌἍἎἏἘἙἚἛἜἝἨἩἪἫἬἭἮἯἸἹἺἻἼἽἾἿὈὉὊὋὌὍὙὛὝὟὨὩὪὫὬὭὮὯᾈᾉᾊᾋᾌᾍᾎᾏᾘᾙᾚᾛᾜᾝᾞᾟᾨᾩᾪᾫᾬᾭᾮᾯᾸᾹᾺΆᾼῈΈῊΉῌῘῙῚΊῨῩῪΎῬῸΌῺΏῼΩKÅℲⅠⅡⅢⅣⅤⅥⅦⅧⅨⅩⅪⅫⅬⅭⅮⅯↃⒶⒷⒸⒹⒺⒻⒼⒽⒾⒿⓀⓁⓂⓃⓄⓅⓆⓇⓈⓉⓊⓋⓌⓍⓎⓏⰀⰁⰂⰃⰄⰅⰆⰇⰈⰉⰊⰋⰌⰍⰎⰏⰐⰑⰒⰓⰔⰕⰖⰗⰘⰙⰚⰛⰜⰝⰞⰟⰠⰡⰢⰣⰤⰥⰦⰧⰨⰩⰪⰫⰬⰭⰮⰯⱠⱢⱣⱤⱧⱩⱫⱭⱮⱯⱰⱲⱵⱾⱿⲀⲂⲄⲆⲈⲊⲌⲎⲐⲒⲔⲖⲘⲚⲜⲞⲠⲢⲤⲦⲨⲪⲬⲮⲰⲲⲴⲶⲸⲺⲼⲾⳀⳂⳄⳆⳈⳊⳌⳎⳐⳒⳔⳖⳘⳚⳜⳞⳠⳢⳫⳭⳲꙀꙂꙄꙆꙈꙊꙌꙎꙐꙒꙔꙖꙘꙚꙜꙞꙠꙢꙤꙦꙨꙪꙬꚀꚂꚄꚆꚈꚊꚌꚎꚐꚒꚔꚖꚘꚚꜢꜤꜦꜨꜪꜬꜮꜲꜴꜶꜸꜺꜼꜾꝀꝂꝄꝆꝈꝊꝌꝎꝐꝒꝔꝖꝘꝚꝜꝞꝠꝢꝤꝦꝨꝪꝬꝮꝹꝻꝽꝾꞀꞂꞄꞆꞋꞍꞐꞒꞖꞘꞚꞜꞞꞠꞢꞤꞦꞨꞪꞫꞬꞭꞮꞰꞱꞲꞳꞴꞶꞸꞺꞼꞾꟀꟂꟄꟅꟆꟇꟉꟐꟖꟘꟵＡＢＣＤＥＦＧＨＩＪＫＬＭＮＯＰＱＲＳＴＵＶＷＸＹＺ𐐀𐐁𐐂𐐃𐐄𐐅𐐆𐐇𐐈𐐉𐐊𐐋𐐌𐐍𐐎𐐏𐐐𐐑𐐒𐐓𐐔𐐕𐐖𐐗𐐘𐐙𐐚𐐛𐐜𐐝𐐞𐐟𐐠𐐡𐐢𐐣𐐤𐐥𐐦𐐧𐒰𐒱𐒲𐒳𐒴𐒵𐒶𐒷𐒸𐒹𐒺𐒻𐒼𐒽𐒾𐒿𐓀𐓁𐓂𐓃𐓄𐓅𐓆𐓇𐓈𐓉𐓊𐓋𐓌𐓍𐓎𐓏𐓐𐓑𐓒𐓓𐕰𐕱𐕲𐕳𐕴𐕵𐕶𐕷𐕸𐕹𐕺𐕼𐕽𐕾𐕿𐖀𐖁𐖂𐖃𐖄𐖅𐖆𐖇𐖈𐖉𐖊𐖌𐖍𐖎𐖏𐖐𐖑𐖒𐖔𐖕𐲀𐲁𐲂𐲃𐲄𐲅𐲆𐲇𐲈𐲉𐲊𐲋𐲌𐲍𐲎𐲏𐲐𐲑𐲒𐲓𐲔𐲕𐲖𐲗𐲘𐲙𐲚𐲛𐲜𐲝𐲞𐲟𐲠𐲡𐲢𐲣𐲤𐲥𐲦𐲧𐲨𐲩𐲪𐲫𐲬𐲭𐲮𐲯𐲰𐲱𐲲𑢠𑢡𑢢𑢣𑢤𑢥𑢦𑢧𑢨𑢩𑢪𑢫𑢬𑢭𑢮𑢯𑢰𑢱𑢲𑢳𑢴𑢵𑢶𑢷𑢸𑢹𑢺𑢻𑢼𑢽𑢾𑢿𖹀𖹁𖹂𖹃𖹄𖹅𖹆𖹇𖹈𖹉𖹊𖹋𖹌𖹍𖹎𖹏𖹐𖹑𖹒𖹓𖹔𖹕𖹖𖹗𖹘𖹙𖹚𖹛𖹜𖹝𖹞𖹟𞤀𞤁𞤂𞤃𞤄𞤅𞤆𞤇𞤈𞤉𞤊𞤋𞤌𞤍𞤎𞤏𞤐𞤑𞤒𞤓𞤔𞤕𞤖𞤗𞤘𞤙𞤚𞤛𞤜𞤝𞤞𞤟𞤠𞤡', 'abcdefghijklmnopqrstuvwxyzàáâãäåæçèéêëìíîïðñòóôõöøùúûüýþāăąćĉċčďđēĕėęěĝğġģĥħĩīĭįĳĵķĺļľŀłńņňŋōŏőœŕŗřśŝşšţťŧũūŭůűųŵŷÿźżžɓƃƅɔƈɖɗƌǝəɛƒɠɣɩɨƙɯɲɵơƣƥʀƨʃƭʈưʊʋƴƶʒƹƽǆǆǉǉǌǌǎǐǒǔǖǘǚǜǟǡǣǥǧǩǫǭǯǳǳǵƕƿǹǻǽǿȁȃȅȇȉȋȍȏȑȓȕȗșțȝȟƞȣȥȧȩȫȭȯȱȳⱥȼƚⱦɂƀʉʌɇɉɋɍɏͱͳͷϳάέήίόύώαβγδεζηθικλμνξοπρστυφχψωϊϋϗϙϛϝϟϡϣϥϧϩϫϭϯθϸϲϻͻͼͽѐёђѓєѕіїјљњћќѝўџабвгдежзийклмнопрстуфхцчшщъыьэюяѡѣѥѧѩѫѭѯѱѳѵѷѹѻѽѿҁҋҍҏґғҕҗҙқҝҟҡңҥҧҩҫҭүұҳҵҷҹһҽҿӏӂӄӆӈӊӌӎӑӓӕӗәӛӝӟӡӣӥӧөӫӭӯӱӳӵӷӹӻӽӿԁԃԅԇԉԋԍԏԑԓԕԗԙԛԝԟԡԣԥԧԩԫԭԯաբգդեզէըթժիլխծկհձղճմյնշոչպջռսվտրցւփքօֆⴀⴁⴂⴃⴄⴅⴆⴇⴈⴉⴊⴋⴌⴍⴎⴏⴐⴑⴒⴓⴔⴕⴖⴗⴘⴙⴚⴛⴜⴝⴞⴟⴠⴡⴢⴣⴤⴥⴧⴭꭰꭱꭲꭳꭴꭵꭶꭷꭸꭹꭺꭻꭼꭽꭾꭿꮀꮁꮂꮃꮄꮅꮆꮇꮈꮉꮊꮋꮌꮍꮎꮏꮐꮑꮒꮓꮔꮕꮖꮗꮘꮙꮚꮛꮜꮝꮞꮟꮠꮡꮢꮣꮤꮥꮦꮧꮨꮩꮪꮫꮬꮭꮮꮯꮰꮱꮲꮳꮴꮵꮶꮷꮸꮹꮺꮻꮼꮽꮾꮿᏸᏹᏺᏻᏼᏽაბგდევზთიკლმნოპჟრსტუფქღყშჩცძწჭხჯჰჱჲჳჴჵჶჷჸჹჺჽჾჿḁḃḅḇḉḋḍḏḑḓḕḗḙḛḝḟḡḣḥḧḩḫḭḯḱḳḵḷḹḻḽḿṁṃṅṇṉṋṍṏṑṓṕṗṙṛṝṟṡṣṥṧṩṫṭṯṱṳṵṷṹṻṽṿẁẃẅẇẉẋẍẏẑẓẕßạảấầẩẫậắằẳẵặẹẻẽếềểễệỉịọỏốồổỗộớờởỡợụủứừửữựỳỵỷỹỻỽỿἀἁἂἃἄἅἆἇἐἑἒἓἔἕἠἡἢἣἤἥἦἧἰἱἲἳἴἵἶἷὀὁὂὃὄὅὑὓὕὗὠὡὢὣὤὥὦὧᾀᾁᾂᾃᾄᾅᾆᾇᾐᾑᾒᾓᾔᾕᾖᾗᾠᾡᾢᾣᾤᾥᾦᾧᾰᾱὰάᾳὲέὴήῃῐῑὶίῠῡὺύῥὸόὼώῳωkåⅎⅰⅱⅲⅳⅴⅵⅶⅷⅸⅹⅺⅻⅼⅽⅾⅿↄⓐⓑⓒⓓⓔⓕⓖⓗⓘⓙⓚⓛⓜⓝⓞⓟⓠⓡⓢⓣⓤⓥⓦⓧⓨⓩⰰⰱⰲⰳⰴⰵⰶⰷⰸⰹⰺⰻⰼⰽⰾⰿⱀⱁⱂⱃⱄⱅⱆⱇⱈⱉⱊⱋⱌⱍⱎⱏⱐⱑⱒⱓⱔⱕⱖⱗⱘⱙⱚⱛⱜⱝⱞⱟⱡɫᵽɽⱨⱪⱬɑɱɐɒⱳⱶȿɀⲁⲃⲅⲇⲉⲋⲍⲏⲑⲓⲕⲗⲙⲛⲝⲟⲡⲣⲥⲧⲩⲫⲭⲯⲱⲳⲵⲷⲹⲻⲽⲿⳁⳃⳅⳇⳉⳋⳍⳏⳑⳓⳕⳗⳙⳛⳝⳟⳡⳣⳬⳮⳳꙁꙃꙅꙇꙉꙋꙍꙏꙑꙓꙕꙗꙙꙛꙝꙟꙡꙣꙥꙧꙩꙫꙭꚁꚃꚅꚇꚉꚋꚍꚏꚑꚓꚕꚗꚙꚛꜣꜥꜧꜩꜫꜭꜯꜳꜵꜷꜹꜻꜽꜿꝁꝃꝅꝇꝉꝋꝍꝏꝑꝓꝕꝗꝙꝛꝝꝟꝡꝣꝥꝧꝩꝫꝭꝯꝺꝼᵹꝿꞁꞃꞅꞇꞌɥꞑꞓꞗꞙꞛꞝꞟꞡꞣꞥꞧꞩɦɜɡɬɪʞʇʝꭓꞵꞷꞹꞻꞽꞿꟁꟃꞔʂᶎꟈꟊꟑꟗꟙꟶａｂｃｄｅｆｇｈｉｊｋｌｍｎｏｐｑｒｓｔｕｖｗｘｙｚ𐐨𐐩𐐪𐐫𐐬𐐭𐐮𐐯𐐰𐐱𐐲𐐳𐐴𐐵𐐶𐐷𐐸𐐹𐐺𐐻𐐼𐐽𐐾𐐿𐑀𐑁𐑂𐑃𐑄𐑅𐑆𐑇𐑈𐑉𐑊𐑋𐑌𐑍𐑎𐑏𐓘𐓙𐓚𐓛𐓜𐓝𐓞𐓟𐓠𐓡𐓢𐓣𐓤𐓥𐓦𐓧𐓨𐓩𐓪𐓫𐓬𐓭𐓮𐓯𐓰𐓱𐓲𐓳𐓴𐓵𐓶𐓷𐓸𐓹𐓺𐓻𐖗𐖘𐖙𐖚𐖛𐖜𐖝𐖞𐖟𐖠𐖡𐖣𐖤𐖥𐖦𐖧𐖨𐖩𐖪𐖫𐖬𐖭𐖮𐖯𐖰𐖱𐖳𐖴𐖵𐖶𐖷𐖸𐖹𐖻𐖼𐳀𐳁𐳂𐳃𐳄𐳅𐳆𐳇𐳈𐳉𐳊𐳋𐳌𐳍𐳎𐳏𐳐𐳑𐳒𐳓𐳔𐳕𐳖𐳗𐳘𐳙𐳚𐳛𐳜𐳝𐳞𐳟𐳠𐳡𐳢𐳣𐳤𐳥𐳦𐳧𐳨𐳩𐳪𐳫𐳬𐳭𐳮𐳯𐳰𐳱𐳲𑣀𑣁𑣂𑣃𑣄𑣅𑣆𑣇𑣈𑣉𑣊𑣋𑣌𑣍𑣎𑣏𑣐𑣑𑣒𑣓𑣔𑣕𑣖𑣗𑣘𑣙𑣚𑣛𑣜𑣝𑣞𑣟𖹠𖹡𖹢𖹣𖹤𖹥𖹦𖹧𖹨𖹩𖹪𖹫𖹬𖹭𖹮𖹯𖹰𖹱𖹲𖹳𖹴𖹵𖹶𖹷𖹸𖹹𖹺𖹻𖹼𖹽𖹾𖹿𞤢𞤣𞤤𞤥𞤦𞤧𞤨𞤩𞤪𞤫𞤬𞤭𞤮𞤯𞤰𞤱𞤲𞤳𞤴𞤵𞤶𞤷𞤸𞤹𞤺𞤻𞤼𞤽𞤾𞤿𞥀𞥁𞥂𞥃') COLLATE "C";
$normalize$;

LOCK TABLE "Users" IN SHARE ROW EXCLUSIVE MODE;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM "Users"
        GROUP BY queueflow_normalize_email("Email") COLLATE "C"
        HAVING count(*) > 1
    ) THEN
        RAISE EXCEPTION 'Cannot create global Users.Email unique index: duplicate normalized emails exist. Run scripts/preflight-global-user-email.sql and resolve duplicates first.';
    END IF;
    IF EXISTS (SELECT 1 FROM "Users" WHERE "Email" COLLATE "C" <> queueflow_normalize_email("Email") COLLATE "C") THEN
        RAISE EXCEPTION 'Noncanonical user emails exist. Review the preflight report; migration never changes user data automatically.';
    END IF;
END $$;

DROP INDEX "IX_Users_OrganizationId_Email";

CREATE TABLE "OrganizationInvitations" (
    "Id" uuid NOT NULL,
    "Email" character varying(320) NOT NULL,
    "ResponsibleName" character varying(200),
    "OrganizationName" character varying(200),
    "Plan" character varying(100),
    "TokenHash" character varying(200) NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "VerificationCodeHash" character varying(200),
    "VerificationCodeExpiresAt" timestamp with time zone,
    "VerificationCodeSentAt" timestamp with time zone,
    "VerifiedAt" timestamp with time zone,
    "UsedAt" timestamp with time zone,
    "RevokedAt" timestamp with time zone,
    "AttemptCount" integer NOT NULL,
    "LastAttemptAt" timestamp with time zone,
    "ActivatedOrganizationId" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    CONSTRAINT "PK_OrganizationInvitations" PRIMARY KEY ("Id")
);

CREATE TABLE "PlatformAuditLogs" (
    "Id" uuid NOT NULL,
    "PlatformUserId" uuid,
    "Action" character varying(200) NOT NULL,
    "ResourceType" character varying(200) NOT NULL,
    "ResourceId" uuid,
    "Data" text,
    "CorrelationId" character varying(200) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    CONSTRAINT "PK_PlatformAuditLogs" PRIMARY KEY ("Id")
);

CREATE TABLE "PlatformRefreshTokens" (
    "Id" uuid NOT NULL,
    "PlatformUserId" uuid NOT NULL,
    "TokenHash" character varying(200) NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "RevokedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    CONSTRAINT "PK_PlatformRefreshTokens" PRIMARY KEY ("Id")
);

CREATE TABLE "PlatformUsers" (
    "Id" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Email" character varying(320) NOT NULL,
    "PasswordHash" character varying(2000) NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    CONSTRAINT "PK_PlatformUsers" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX "IX_Users_Email" ON "Users" ("Email");

CREATE UNIQUE INDEX "IX_OrganizationInvitations_ActivatedOrganizationId" ON "OrganizationInvitations" ("ActivatedOrganizationId") WHERE "ActivatedOrganizationId" IS NOT NULL;

CREATE INDEX "IX_OrganizationInvitations_Email_ExpiresAt" ON "OrganizationInvitations" ("Email", "ExpiresAt");

CREATE UNIQUE INDEX "IX_OrganizationInvitations_TokenHash" ON "OrganizationInvitations" ("TokenHash");

CREATE INDEX "IX_PlatformAuditLogs_CreatedAt" ON "PlatformAuditLogs" ("CreatedAt");

CREATE INDEX "IX_PlatformAuditLogs_PlatformUserId_CreatedAt" ON "PlatformAuditLogs" ("PlatformUserId", "CreatedAt");

CREATE INDEX "IX_PlatformRefreshTokens_PlatformUserId_ExpiresAt" ON "PlatformRefreshTokens" ("PlatformUserId", "ExpiresAt");

CREATE UNIQUE INDEX "IX_PlatformRefreshTokens_TokenHash" ON "PlatformRefreshTokens" ("TokenHash");

CREATE UNIQUE INDEX "IX_PlatformUsers_Email" ON "PlatformUsers" ("Email");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260915060310_AddPlatformAdministrationAndInvitations', '10.0.0');

COMMIT;

START TRANSACTION;
ALTER TABLE "Organizations" ADD "OnboardingCompletedAt" timestamp with time zone;

ALTER TABLE "OrganizationInvitations" ADD "ActivationAuthorizationConsumedAt" timestamp with time zone;

ALTER TABLE "OrganizationInvitations" ADD "ActivationAuthorizationExpiresAt" timestamp with time zone;

ALTER TABLE "OrganizationInvitations" ADD "ActivationAuthorizationHash" character varying(200);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260915201718_SecureActivationAndOnboarding', '10.0.0');

COMMIT;

START TRANSACTION;
ALTER TABLE "Users" ADD CONSTRAINT "CK_Users_CanonicalEmail" CHECK ("Email" COLLATE "C" = queueflow_normalize_email("Email") COLLATE "C");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260916010353_EnforceCanonicalUserEmail', '10.0.0');

COMMIT;
