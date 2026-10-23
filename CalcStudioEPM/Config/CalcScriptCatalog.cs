using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace CalcStudioEPM.Config
{
    /// <summary>A calc-script function and how many parameters it takes.</summary>
    public class CalcFunction
    {
        public string Name    { get; set; }   // with the @, e.g. "@RELATIVE"
        public int    MinArgs { get; set; }
        /// <summary>-1 = any number.</summary>
        public int    MaxArgs { get; set; }
        public string Syntax  { get; set; }
        public string Source  { get; set; }   // "Essbase", "Calculation Manager", "Planning", "custom"
    }

    /// <summary>A command that opens a block, e.g. FIX … ENDFIX, IF … ELSEIF/ELSE … ENDIF.</summary>
    public class CalcBlock
    {
        public string       Open   { get; set; }
        public string       Close  { get; set; }
        public List<string> Middle { get; set; } = new List<string>();
        /// <summary>True when the block must sit inside a member block: "Mbr" ( IF … ENDIF; ).</summary>
        public bool         RequiresMemberBlock { get; set; }
    }

    /// <summary>
    /// Everything the calc-script checker knows — functions with parameter counts, block
    /// commands, plain commands and SET options — plus rule severities. Stored in
    /// %AppData%\Notepad++\plugins\CalcStudioEPM\calcscript_catalog.json; "Edit rules…" in
    /// the validation window opens it. Entries you change or add are kept; entries a newer
    /// plugin version introduces are merged in.
    /// </summary>
    public class CalcScriptCatalog
    {
        public Dictionary<string, string> Rules { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public List<CalcFunction> Functions  { get; set; } = new List<CalcFunction>();
        /// <summary>@-names used as values, not called: @_TRUE, @_FALSE …</summary>
        public List<string>       Constants  { get; set; } = new List<string>();
        public List<CalcBlock>    Blocks     { get; set; } = new List<CalcBlock>();
        public List<string>       Commands   { get; set; } = new List<string>();
        public List<string>       SetOptions { get; set; } = new List<string>();

        public static readonly Dictionary<string, string> RuleDescriptions = new Dictionary<string, string>
        {
            ["syntax.unbalanced-brackets"]     = "( [ without a matching ) ] (or the reverse)",
            ["syntax.unterminated-string"]     = "a \"member name\" that is never closed",
            ["syntax.unterminated-comment"]    = "a /* comment that is never closed",
            ["syntax.unterminated-prompt"]     = "a {runtime prompt} that is never closed",
            ["syntax.unterminated-expression"] = "a statement missing its closing ;",
            ["block.unbalanced"]               = "FIX / IF / LOOP / EXCLUDE without its END… (or the reverse)",
            ["block.outside-member-block"]     = "IF used outside a member block \"Mbr\"( … );",
            ["function.unknown"]               = "an @function that is not in the catalog",
            ["function.argument-count"]        = "an @function called with too few or too many parameters",
            ["function.missing-parentheses"]   = "an @function used without ( )",
            ["command.unknown-set"]            = "a SET option that is not in the catalog",
            ["rtp.unknown-variable"]           = "a {prompt} that is not in the connection's variable dictionary",
            ["template.missing-argument"]      = "a %Template call without name:=, application:= or plantype:=",
            ["template.not-found"]             = "a %Template call to a template that is not in the last pull",
            ["template.unknown-dtp"]           = "a %Template call passing a design-time prompt the template doesn't declare",
            ["block.command-in-member-block"]  = "FIX, SET, DATACOPY, AGG, CALC … inside a \"Member\"( … ) block",
            ["if.assignment"]                  = "= (assignment) instead of == in an IF / ELSEIF condition",
            ["fixparallel.nested"]             = "a FIXPARALLEL inside another FIXPARALLEL",
            ["threadvar.outside-fixparallel"]  = "THREADVAR in a script without FIXPARALLEL",
            ["datacopy.missing-to"]            = "DATACOPY without TO",
            ["fix.empty"]                      = "FIX () / EXCLUDE () with no members",
            ["text.smart-characters"]          = "curly quotes, en/em dashes or non-breaking spaces in code (pasted from Word / Outlook)",
            ["subvar.unknown"]                 = "an &variable that is not a substitution variable (needs a pull)",
            ["subvar.wrong-cube"]              = "an &variable defined only for other cubes",
            ["member.unknown"]                 = "a \"member\" that is not a member or alias of any dimension (needs dimensions on Pull)",
            ["member.unknown-unquoted"]        = "an unquoted member name that is not a member or alias (noisier: off by default)",
            ["member.not-in-cube"]             = "a member of the dimension that is not valid in the rule's cube",
            ["syntax.missing-semicolon"]       = "a formula or command not ended with ';' (FIX, IF, LOOP, their ENDs and \"Member\"( … ) blocks need none)",
        };

        public string SeverityOf(string ruleId) =>
            Rules.TryGetValue(ruleId, out string s) ? (s ?? "off").Trim().ToLowerInvariant() : "warning";

        // ------------------------------------------------------------------ //
        //  Defaults
        // ------------------------------------------------------------------ //

        public static CalcScriptCatalog Defaults()
        {
            var c = new CalcScriptCatalog
            {
                Rules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["syntax.unbalanced-brackets"]    = "error",
                    ["syntax.unterminated-string"]    = "error",
                    ["syntax.unterminated-comment"]   = "error",
                    ["syntax.unterminated-prompt"]    = "error",
                    ["syntax.unterminated-expression"]= "error",
                    ["block.unbalanced"]              = "error",
                    ["block.outside-member-block"]    = "error",
                    ["function.unknown"]              = "error",
                    ["function.argument-count"]       = "error",
                    ["function.missing-parentheses"]  = "error",
                    ["command.unknown-set"]           = "warning",
                    ["rtp.unknown-variable"]          = "warning",
                    ["template.missing-argument"]     = "error",
                    ["template.not-found"]            = "warning",
                    ["template.unknown-dtp"]          = "warning",
                    ["block.command-in-member-block"] = "error",
                    ["if.assignment"]                 = "error",
                    ["fixparallel.nested"]            = "error",
                    ["threadvar.outside-fixparallel"] = "warning",
                    ["datacopy.missing-to"]           = "error",
                    ["fix.empty"]                     = "error",
                    ["text.smart-characters"]         = "error",
                    ["subvar.unknown"]                = "warning",
                    ["subvar.wrong-cube"]             = "warning",
                    ["member.unknown"]                = "warning",
                    ["member.unknown-unquoted"]       = "off",
                    ["member.not-in-cube"]            = "warning",
                    ["syntax.missing-semicolon"]      = "error",
                },
                Blocks = new List<CalcBlock>
                {
                    new CalcBlock { Open = "FIX",         Close = "ENDFIX" },
                    new CalcBlock { Open = "FIXPARALLEL", Close = "ENDFIXPARALLEL" },
                    new CalcBlock { Open = "EXCLUDE",     Close = "ENDEXCLUDE" },
                    new CalcBlock { Open = "LOOP",        Close = "ENDLOOP" },
                    new CalcBlock { Open = "IF",          Close = "ENDIF", Middle = new List<string> { "ELSEIF", "ELSE" },
                                    RequiresMemberBlock = true },
                },
                // Essbase calculation command list (docs: esscq/calculation-command-list)
                Commands = new List<string>
                {
                    "AGG", "ARRAY", "CALC ALL", "CALC AVERAGE", "CALC DIM", "CALC FIRST", "CALC LAST",
                    "CALC TWOPASS", "CCONV", "CLEARBLOCK", "CLEARCCTRACK", "CLEARDATA", "DATACOPY",
                    "DATAEXPORT", "DATAEXPORTCOND", "DATAIMPORTBIN", "DATAMERGE", "POSTFIXPARALLEL",
                    "THREADVAR", "USE_MDX_INSERT", "VAR",
                },
                SetOptions = new List<string>
                {
                    "AGGMISSG", "CACHE", "CALCDIAGNOSTICS", "CALCPARALLEL", "CALCTASKDIMS", "CCTRACKCALC",
                    "CLEARUPDATESTATUS", "COPYMISSINGBLOCK", "CREATEBLOCKONEQ", "CREATENONMISSINGBLK",
                    "DATAEXPORTOPTIONS", "DATAIMPORTIGNORETIMESTAMP", "EMPTYMEMBERSETS", "FRMLBOTTOMUP",
                    "FRMLRTDYNAMIC", "HYBRIDBSOINCALCSCRIPT", "MSG", "NOTICE", "REMOTECALC",
                    "RUNTIMESUBVARS", "SCAPERSPECTIVE", "TRACE", "TRACEID", "UPDATECALC", "UPTOLOCAL",
                    // older options still accepted by Essbase
                    "LOCKBLOCK", "CALCHASHTBL", "MSG", "NOTICE",
                },
            };
            c.Functions = EssbaseFunctions();
            c.Functions.AddRange(CalcManagerFunctions.All());
            c.Functions.AddRange(PlanningFunctions());
            c.Constants = new List<string> { "@_TRUE", "@_FALSE" };
            return c;
        }

        /// <summary>Planning (HSP) functions available to calc scripts on EPM Planning.</summary>
        private static List<CalcFunction> PlanningFunctions() => new List<CalcFunction>
        {
            new CalcFunction { Name = "@HSPNUMTOSTRING", MinArgs = 1, MaxArgs = 1, Syntax = "@HspNumToString(number)", Source = "Planning" },
        };

        /// <summary>
        /// Essbase calculation functions supported by Calculation Manager
        /// (docs: ecalc/essbase_functions_supported_by_calc_mgr) with parameter counts.
        /// MaxArgs -1 = a list of any length.
        /// </summary>
        private static List<CalcFunction> EssbaseFunctions()
        {
            var list = new List<CalcFunction>();
            void F(string name, int min, int max, string syntax) =>
                list.Add(new CalcFunction { Name = name, MinArgs = min, MaxArgs = max, Syntax = syntax, Source = "Essbase" });

            F("@ABS", 1, 1, "@ABS(expression)");
            F("@ACCUM", 2, 2, "@ACCUM(mbrName, rangeList)");
            F("@ALIAS", 1, 2, "@ALIAS(mbrName [, altAliasTable])");
            F("@ALLANCESTORS", 1, 1, "@ALLANCESTORS(mbrName)");
            F("@ALLOCATE", 4, 9, "@ALLOCATE(amount, allocationRange, basisMbr, [roundMbr], method [, methodParams] [, round [, numDigits] [, roundErr]])");
            F("@ANCEST", 2, 3, "@ANCEST(dimName, genLevNum [, mbrName])");
            F("@ANCESTORS", 1, 2, "@ANCESTORS(mbrName [, genLevNum | genLevName])");
            F("@ANCESTVAL", 2, 3, "@ANCESTVAL(dimName, genLevNum [, mbrName])");
            F("@ATTRIBUTE", 1, 1, "@ATTRIBUTE(attMbrName)");
            F("@ATTRIBUTEBVAL", 1, 1, "@ATTRIBUTEBVAL(attDimName)");
            F("@ATTRIBUTESVAL", 1, 1, "@ATTRIBUTESVAL(attDimName)");
            F("@ATTRIBUTEVAL", 1, 1, "@ATTRIBUTEVAL(attDimName)");
            F("@AVG", 2, -1, "@AVG(SKIPNONE | SKIPMISSING | SKIPZERO | SKIPBOTH, expList)");
            F("@AVGRANGE", 2, 3, "@AVGRANGE(SKIPNONE | SKIPMISSING | SKIPZERO | SKIPBOTH, mbrName [, XrangeList])");
            F("@BETWEEN", 2, 3, "@BETWEEN(mbrName1, mbrName2)");
            F("@CALCMODE", 1, 1, "@CALCMODE(CELL | BLOCK | TOPDOWN | BOTTOMUP)");
            F("@CHILDREN", 1, 1, "@CHILDREN(mbrName)");
            F("@COMPOUND", 2, 3, "@COMPOUND(balanceMbr, rateMbrConst [, XrangeList])");
            F("@COMPOUNDGROWTH", 2, 3, "@COMPOUNDGROWTH(principalMbr, rateMbrConst [, XrangeList])");
            F("@CONCATENATE", 2, 2, "@CONCATENATE(String1, String2)");
            F("@CORRELATION", 3, 3, "@CORRELATION(SKIPNONE | SKIPMISSING | SKIPZERO | SKIPBOTH, expList1, expList2)");
            F("@COUNT", 2, -1, "@COUNT(SKIPNONE | SKIPMISSING | SKIPZERO | SKIPBOTH, expList)");
            F("@CREATEBLOCK", 1, -1, "@CREATEBLOCK(mbrList)");
            F("@CURGEN", 1, 1, "@CURGEN(dimName)");
            F("@CURLEV", 1, 1, "@CURLEV(dimName)");
            F("@CURRMBR", 1, 1, "@CURRMBR(dimName)");
            F("@CURRMBRRANGE", 3, 5, "@CURRMBRRANGE(dimName, {GEN | LEV}, genLevNum, [startOffset], [endOffset])");
            F("@DATEDIFF", 3, 3, "@DATEDIFF(date1, date2, date_part)");
            F("@DATEPART", 2, 3, "@DATEPART(date, date_part [, locale])");
            F("@DATEROLL", 3, 3, "@DATEROLL(date, date_part, number)");
            F("@DECLINE", 4, 5, "@DECLINE(costMbr, salvageMbrConst, lifeMbrConst, factorMbrConst [, XrangeList])");
            F("@DESCENDANTS", 1, 2, "@DESCENDANTS(mbrName [, genLevNum | genLevName])");
            F("@DISCOUNT", 3, 4, "@DISCOUNT(cashMbr, rateMbrConst, dateMbrConst [, XrangeList])");
            F("@ENUMVALUE", 1, 2, "@ENUMVALUE(enumList, enumValue)");
            F("@EQUAL", 2, 2, "@EQUAL(mbrName, mbrName)");
            F("@EXP", 1, 1, "@EXP(expression)");
            F("@EXPAND", 2, 2, "@EXPAND(rangeFunction, dimName)");
            F("@FACTORIAL", 1, 1, "@FACTORIAL(expression)");
            F("@FORMATDATE", 2, 2, "@FORMATDATE(date, format)");
            F("@GEN", 1, 1, "@GEN(mbrName)");
            F("@GENMBRS", 2, 2, "@GENMBRS(dimName, genName | genNum)");
            F("@GROWTH", 2, 3, "@GROWTH(principalMbr, rateMbrConst [, XrangeList])");
            F("@IALLANCESTORS", 1, 1, "@IALLANCESTORS(mbrName)");
            F("@IANCESTORS", 1, 2, "@IANCESTORS(mbrName [, genLevNum | genLevName])");
            F("@ICHILDREN", 1, 1, "@ICHILDREN(mbrName)");
            F("@IDESCENDANTS", 1, 2, "@IDESCENDANTS(mbrName [, genLevNum | genLevName])");
            F("@ILANCESTORS", 1, -1, "@ILANCESTORS(mbrList [, genLevNum])");
            F("@ILDESCENDANTS", 1, -1, "@ILDESCENDANTS(mbrList [, genLevNum])");
            F("@ILSIBLINGS", 1, 1, "@ILSIBLINGS(mbrName)");
            F("@INT", 1, 1, "@INT(expression)");
            F("@INTEREST", 2, 3, "@INTEREST(cashMbr, rateMbrConst [, XrangeList])");
            F("@INTERSECT", 2, 2, "@INTERSECT(list1, list2)");
            F("@IRDESCENDANTS", 1, 2, "@IRDESCENDANTS(mbrName [, genLevNum | genLevName])");
            F("@IRR", 1, 2, "@IRR(cashflowMbr [, discountFlag])");
            F("@IRREX", 1, 3, "@IRREX(cashflowMbr [, guess] [, XrangeList])");
            F("@IRSIBLINGS", 1, 1, "@IRSIBLINGS(mbrName)");
            F("@ISACCTYPE", 1, 1, "@ISACCTYPE(acctTag)");
            F("@ISANCEST", 1, 1, "@ISANCEST(mbrName)");
            F("@ISATTRIBUTE", 1, 1, "@ISATTRIBUTE(attMbrName)");
            F("@ISCHILD", 1, 1, "@ISCHILD(mbrName)");
            F("@ISDESC", 1, 1, "@ISDESC(mbrName)");
            F("@ISGEN", 2, 2, "@ISGEN(dimName, genName | genNum)");
            F("@ISIANCEST", 1, 1, "@ISIANCEST(mbrName)");
            F("@ISIBLINGS", 1, 1, "@ISIBLINGS(mbrName)");
            F("@ISICHILD", 1, 1, "@ISICHILD(mbrName)");
            F("@ISIDESC", 1, 1, "@ISIDESC(mbrName)");
            F("@ISIPARENT", 1, 1, "@ISIPARENT(mbrName)");
            F("@ISISIBLING", 1, 1, "@ISISIBLING(mbrName)");
            F("@ISLEV", 2, 2, "@ISLEV(dimName, levName | levNum)");
            F("@ISMBR", 1, -1, "@ISMBR(mbrName | rangeList | mbrList)");
            F("@ISMBRUDA", 2, 2, "@ISMBRUDA(mbrName, UDAStr)");
            F("@ISMBRWITHATTR", 3, 3, "@ISMBRWITHATTR(dimName, operator, value)");
            F("@ISPARENT", 1, 1, "@ISPARENT(mbrName)");
            F("@ISRANGENONEMPTY", 1, -1, "@ISRANGENONEMPTY(XrangeList)");
            F("@ISSAMEGEN", 1, 1, "@ISSAMEGEN(mbrName)");
            F("@ISSAMELEV", 1, 1, "@ISSAMELEV(mbrName)");
            F("@ISSIBLING", 1, 1, "@ISSIBLING(mbrName)");
            F("@ISUDA", 2, 2, "@ISUDA(dimName, UDAStr)");
            F("@LANCESTORS", 1, -1, "@LANCESTORS(mbrList [, genLevNum])");
            F("@LDESCENDANTS", 1, -1, "@LDESCENDANTS(mbrList [, genLevNum])");
            F("@LEV", 1, 1, "@LEV(mbrName)");
            F("@LEVMBRS", 2, 2, "@LEVMBRS(dimName, levName | levNum)");
            F("@LIKE", 2, 3, "@LIKE(string, pattern [, dimName])");
            F("@LIST", 1, -1, "@LIST(argument, argument, ...)");
            F("@LN", 1, 1, "@LN(expression)");
            F("@LOG", 1, 2, "@LOG(expression [, base])");
            F("@LOG10", 1, 1, "@LOG10(expression)");
            F("@LSIBLINGS", 1, 1, "@LSIBLINGS(mbrName)");
            F("@MATCH", 2, 2, "@MATCH(mbrName | genName | levName, \"pattern\")");
            F("@MAX", 1, -1, "@MAX(expList)");
            F("@MAXRANGE", 1, 2, "@MAXRANGE(mbrName [, XrangeList])");
            F("@MAXS", 2, -1, "@MAXS(SKIPNONE | SKIPMISSING | SKIPZERO | SKIPBOTH, expList)");
            F("@MAXSRANGE", 2, 3, "@MAXSRANGE(SKIPNONE | SKIPMISSING | SKIPZERO | SKIPBOTH, mbrName [, XrangeList])");
            F("@MBRCOMPARE", 2, 3, "@MBRCOMPARE(mbrList, tokenString [, mbrList])");
            F("@MBRPARENT", 1, 1, "@MBRPARENT(mbrName)");
            F("@MDALLOCATE", 5, -1, "@MDALLOCATE(amount, Ndim, allocationRange1 … allocationRangeN, basisMbr, [roundMbr], method [, methodParams] [, round [, numDigits] [, roundErr]])");
            F("@MDANCESTVAL", 3, -1, "@MDANCESTVAL(dimCount, dimName1, genLevNum1 … dimNameX, genLevNumX [, mbrName])");
            F("@MDPARENTVAL", 2, -1, "@MDPARENTVAL(numDim, dimName1 … dimNameX [, mbrName])");
            F("@MDSHIFT", 3, -1, "@MDSHIFT(mbrName, n, dimName, [range] [, n, dimName, [range] …])");
            F("@MEDIAN", 2, -1, "@MEDIAN(SKIPNONE | SKIPMISSING | SKIPZERO | SKIPBOTH, expList)");
            F("@MEMBER", 1, 1, "@MEMBER(String)");
            F("@MEMBERAT", 2, 2, "@MEMBERAT(mbrList, mbrPosition)");
            F("@MERGE", 2, 2, "@MERGE(list1, list2)");
            F("@MIN", 1, -1, "@MIN(expList)");
            F("@MINRANGE", 1, 2, "@MINRANGE(mbrName [, XrangeList])");
            F("@MINS", 2, -1, "@MINS(SKIPNONE | SKIPMISSING | SKIPZERO | SKIPBOTH, expList)");
            F("@MINSRANGE", 2, 3, "@MINSRANGE(SKIPNONE | SKIPMISSING | SKIPZERO | SKIPBOTH, mbrName [, XrangeList])");
            F("@MOD", 2, 2, "@MOD(mbrName1, mbrName2)");
            F("@MODE", 2, -1, "@MODE(SKIPNONE | SKIPMISSING | SKIPZERO | SKIPBOTH, expList)");
            F("@MOVAVG", 1, 3, "@MOVAVG(mbrName [, n [, XrangeList]])");
            F("@MOVMAX", 1, 3, "@MOVMAX(mbrName [, n [, XrangeList]])");
            F("@MOVMED", 1, 3, "@MOVMED(mbrName [, n [, XrangeList]])");
            F("@MOVMIN", 1, 3, "@MOVMIN(mbrName [, n [, XrangeList]])");
            F("@MOVSUM", 1, 3, "@MOVSUM(mbrName [, n [, XrangeList]])");
            F("@MOVSUMX", 1, 4, "@MOVSUMX(COPYFORWARD | TRAILMISSING | TRAILSUM, mbrName [, n [, XrangeList]])");
            F("@NAME", 1, 1, "@NAME(mbrName)");
            F("@NEXT", 1, 3, "@NEXT(mbrName [, n, XrangeList])");
            F("@NEXTS", 2, 4, "@NEXTS(SKIPNONE | SKIPMISSING | SKIPZERO | SKIPBOTH, mbrName [, n, XrangeList])");
            F("@NEXTSIBLING", 1, 1, "@NEXTSIBLING(mbrName)");
            F("@NONEMPTYTUPLE", 0, -1, "@NONEMPTYTUPLE([mbrName1, …])");
            F("@NOTEQUAL", 2, 2, "@NOTEQUAL(mbrName, mbrName)");
            F("@NPV", 3, 4, "@NPV(cashflowMbr, rateMbrConst, discountFlag [, XrangeList])");
            F("@PARENT", 1, 2, "@PARENT(dimName [, mbrName])");
            F("@PARENTVAL", 1, 2, "@PARENTVAL(dimName [, mbrName])");
            F("@POWER", 2, 2, "@POWER(expression, power)");
            F("@PREVSIBLING", 1, 1, "@PREVSIBLING(mbrName)");
            F("@PRIOR", 1, 3, "@PRIOR(mbrName [, n, XrangeList])");
            F("@PRIORS", 2, 4, "@PRIORS(SKIPNONE | SKIPMISSING | SKIPZERO | SKIPBOTH, mbrName [, n, XrangeList])");
            F("@PTD", 1, 1, "@PTD(XrangeList)");
            F("@QUERYBOTTOMUP", 0, -1, "@QUERYBOTTOMUP(...)");
            F("@RANGE", 1, 2, "@RANGE(mbrName [, XrangeList])");
            F("@RANGEFIRSTVAL", 2, 2, "@RANGEFIRSTVAL(SKIPNONE | SKIPMISSING | SKIPZERO | SKIPBOTH, XrangeList)");
            F("@RANGELASTVAL", 2, 2, "@RANGELASTVAL(SKIPNONE | SKIPMISSING | SKIPZERO | SKIPBOTH, XrangeList)");
            F("@RANK", 3, -1, "@RANK(SKIPNONE | SKIPMISSING | SKIPZERO | SKIPBOTH, value, expList)");
            F("@RDESCENDANTS", 1, 2, "@RDESCENDANTS(mbrName [, genLevNum | genLevName])");
            F("@RELATIVE", 2, 2, "@RELATIVE(mbrName, genLevNum)");
            F("@RELXRANGE", 2, 2, "@RELXRANGE(startOffset, endOffset)");
            F("@REMAINDER", 1, 1, "@REMAINDER(expression)");
            F("@REMOVE", 2, 2, "@REMOVE(list1, list2)");
            F("@RETURN", 1, 2, "@RETURN(\"ErrorMessage\" [, INFO | ERROR | WARNING])");
            F("@ROUND", 1, 3, "@ROUND(expression [, numDigits [, compatibility]])   (numDigits defaults to 0)");
            F("@RSIBLINGS", 1, 1, "@RSIBLINGS(mbrName)");
            F("@SANCESTVAL", 4, 4, "@SANCESTVAL(rootMbr, genLevNum, SKIPNONE | SKIPMISSING | SKIPZERO | SKIPBOTH, valueMbr)");
            F("@SHARE", 1, -1, "@SHARE(mbrList)");
            F("@SHIFT", 1, 3, "@SHIFT(mbrName [, n [, XrangeList]])");
            F("@SHIFTMINUS", 1, 3, "@SHIFTMINUS(mbrName [, n [, XrangeList]])");
            F("@SHIFTPLUS", 1, 3, "@SHIFTPLUS(mbrName [, n [, XrangeList]])");
            F("@SHIFTSIBLING", 1, 2, "@SHIFTSIBLING(mbrName [, n])");
            F("@SIBLINGS", 1, 1, "@SIBLINGS(mbrName)");
            F("@SLN", 3, 4, "@SLN(costMbr, salvageMbrConst, lifeMbrConst [, XrangeList])");
            F("@SPARENTVAL", 2, 3, "@SPARENTVAL(SKIPNONE | SKIPMISSING | SKIPZERO | SKIPBOTH, dimName [, mbrName])");
            F("@SPLINE", 1, 4, "@SPLINE(YmbrName [, s [, XmbrName [, XrangeList]]])");
            F("@STDEV", 2, -1, "@STDEV(SKIPNONE | SKIPMISSING | SKIPZERO | SKIPBOTH, expList)");
            F("@STDEVP", 2, -1, "@STDEVP(SKIPNONE | SKIPMISSING | SKIPZERO | SKIPBOTH, expList)");
            F("@STDEVRANGE", 2, 3, "@STDEVRANGE(SKIPNONE | SKIPMISSING | SKIPZERO | SKIPBOTH, mbrName [, XrangeList])");
            F("@SUBSTRING", 2, 3, "@SUBSTRING(String, StartPosition [, EndPosition])");
            F("@SUM", 1, -1, "@SUM(expList)");
            F("@SUMRANGE", 1, 2, "@SUMRANGE(mbrName [, XrangeList])");
            F("@SYD", 4, 5, "@SYD(costMbr, salvageMbrConst, lifeMbrConst, periodMbrConst [, XrangeList])");
            F("@TODATE", 2, 2, "@TODATE(formatString, dateString)");
            F("@TODATEEX", 2, 2, "@TODATEEX(formatString, dateString)");
            F("@TODAY", 0, 0, "@TODAY()");
            F("@TREND", 1, -1, "@TREND(Ylist, [Xlist], [weightList], [Xlist2], [XrangeList], [method])");
            F("@TRUNCATE", 1, 1, "@TRUNCATE(expression)");
            F("@UDA", 2, 2, "@UDA(dimName, UDAStr)");
            F("@VAR", 2, 2, "@VAR(mbrName1, mbrName2)");
            F("@VARPER", 2, 2, "@VARPER(mbrName1, mbrName2)");
            F("@VARIANCE", 2, -1, "@VARIANCE(SKIPNONE | SKIPMISSING | SKIPZERO | SKIPBOTH, expList)");
            F("@VARIANCEP", 2, -1, "@VARIANCEP(SKIPNONE | SKIPMISSING | SKIPZERO | SKIPBOTH, expList)");
            F("@WITHATTR", 3, 3, "@WITHATTR(dimName, operator, value)");
            F("@XRANGE", 2, 2, "@XRANGE(mbrName1, mbrName2)");
            F("@XREF", 1, -1, "@XREF(locationAlias [, mbrList])");
            F("@XWRITE", 2, -1, "@XWRITE(expression, locationAlias [, mbrList])");
            return list;
        }

        // ------------------------------------------------------------------ //
        //  Persistence
        // ------------------------------------------------------------------ //

        public static string FilePath => Path.Combine(ConfigManager.SettingsDir, "calcscript_catalog.json");

        public static CalcScriptCatalog Load()
        {
            CalcScriptCatalog defaults = Defaults();
            try
            {
                if (!File.Exists(FilePath))
                {
                    Save(defaults);
                    return defaults;
                }

                var cfg = JsonConvert.DeserializeObject<CalcScriptCatalog>(File.ReadAllText(FilePath, Encoding.UTF8));
                if (cfg == null) return defaults;

                // The user's entries win; anything new in the defaults is merged in
                cfg.Rules = new Dictionary<string, string>(cfg.Rules ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                foreach (var kv in defaults.Rules.Where(kv => !cfg.Rules.ContainsKey(kv.Key))) cfg.Rules[kv.Key] = kv.Value;

                cfg.Functions = cfg.Functions ?? new List<CalcFunction>();
                var known = new HashSet<string>(cfg.Functions.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
                cfg.Functions.AddRange(defaults.Functions.Where(f => !known.Contains(f.Name)));

                cfg.Blocks = cfg.Blocks ?? new List<CalcBlock>();
                var blocks = new HashSet<string>(cfg.Blocks.Select(b => b.Open), StringComparer.OrdinalIgnoreCase);
                cfg.Blocks.AddRange(defaults.Blocks.Where(b => !blocks.Contains(b.Open)));

                cfg.Commands   = Union(cfg.Commands,   defaults.Commands);
                cfg.SetOptions = Union(cfg.SetOptions, defaults.SetOptions);
                cfg.Constants  = Union(cfg.Constants,  defaults.Constants);
                return cfg;
            }
            catch { return defaults; }
        }

        public static void Save(CalcScriptCatalog cfg)
        {
            Directory.CreateDirectory(ConfigManager.SettingsDir);
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(cfg, Formatting.Indented),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        private static List<string> Union(List<string> mine, List<string> defaults) =>
            (mine ?? new List<string>()).Concat(defaults).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
