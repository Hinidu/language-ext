﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using LanguageExt.Parsec;
using static LanguageExt.Prelude;
using static LanguageExt.Parsec.Prim;
using static LanguageExt.Parsec.Char;
using static LanguageExt.Parsec.Expr;
using static LanguageExt.Parsec.Token;
using static LanguageExt.Parsec.Indent;

namespace LanguageExt.Config
{
    public class TypeDef : IEquatable<TypeDef>, IComparable<TypeDef>
    {
        public readonly string Name;
        public readonly Type MapsTo;
        public readonly FuncSpec[] FuncSpecs;
        public readonly TypeDef GenericType;
        public readonly Func<Lst<NamedValueToken>, object> Ctor;
        public readonly Func<ProcessSystemConfigParser, Parser<object>> ValueParser;
        public readonly Func<Lst<object>, object> ToLstValues;
        public readonly Func<Map<string, object>, object> ToMapValues;
        public readonly Map<string, Func<ValueToken, ValueToken, ValueToken>> BinaryOperators;
        public readonly Map<string, Func<ValueToken, ValueToken>> PrefixOperators;
        public readonly Map<string, Func<ValueToken, ValueToken>> PostfixOperators;
        public readonly Map<string, Func<object,object>> ConversionOperators;
        public readonly string NodeName;
        public readonly int Order;

        /// <summary>
        /// Automatically defined record-type constructor
        /// </summary>
        /// <param name="type"></param>
        /// <param name="ctor"></param>
        /// <param name="assembly"></param>
        /// <param name="name"></param>
        /// <param name="nodeName"></param>
        public TypeDef(
            Type type,
            Func<Lst<NamedValueToken>, object> ctor,
            Types assembly,
            int order,
            string name = null,
            string nodeName = ""
            )
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            var info = type.GetTypeInfo();
            Order = order;
            Ctor = ctor ?? (x => x);
            MapsTo = type;
            Name = name ?? MakeName(info.Name);
            BinaryOperators = LanguageExt.Map.empty<string, Func<ValueToken, ValueToken, ValueToken>>();
            PrefixOperators = LanguageExt.Map.empty<string, Func<ValueToken, ValueToken>>();
            PostfixOperators = LanguageExt.Map.empty<string, Func<ValueToken, ValueToken>>();
            ConversionOperators = LanguageExt.Map.empty<string, Func<object, object>>();
            ToLstValues = x => x;
            ToMapValues = x => x;
            GenericType = null;
            NodeName = nodeName;

            var props = from p in type.GetTypeInfo().GetProperties()
                        where assembly.Exists(p.PropertyType)
                        select FuncSpec.Property(MakeName(p.Name), () => assembly.Get(p.PropertyType));

            var fields = from p in type.GetTypeInfo().GetFields()
                         where assembly.Exists(p.FieldType)
                         select FuncSpec.Property(MakeName(p.Name), () => assembly.Get(p.FieldType));

            var methods = from m in type.GetTypeInfo().GetMethods()
                          where m.IsStatic &&
                                assembly.Exists(m.ReturnType) && 
                                m.GetParameters().Map(p => p.ParameterType).ForAll(assembly.Exists)
                          let ps = m.GetParameters().Map(p => new FieldSpec(p.Name, () => assembly.Get(p.ParameterType))).ToArray()
                          select FuncSpec.Attrs(MakeName(m.Name), () => assembly.Get(m.ReturnType), locals => m.Invoke(null, locals.Values.ToArray()), ps);

            FuncSpecs = List.append(props, fields, methods).ToArray();


            ValueParser = BuildObjectParser(FuncSpecs).Memo();
        }

        public TypeDef(
            string name,
            Func<Lst<NamedValueToken>, object> ctor,
            int order,
            params FuncSpec[] funcSpecs
            )
            :
            this(name, ctor, null, order, funcSpecs)
        {
        }

        /// <summary>
        /// Manually defined record-type constructor
        /// </summary>
        /// <param name="name"></param>
        /// <param name="ctor"></param>
        /// <param name="nodeName"></param>
        /// <param name="funcSpecs"></param>
        public TypeDef(
            string name,
            Func<Lst<NamedValueToken>, object> ctor,
            string nodeName,
            int order,
            params FuncSpec[] funcSpecs
            )
        {
            MapsTo = typeof(Lst<NamedValueToken>);
            Name = name;
            Order = order;
            Ctor = ctor ?? (x => x);
            BinaryOperators = LanguageExt.Map.empty<string, Func<ValueToken, ValueToken, ValueToken>>();
            PrefixOperators = LanguageExt.Map.empty<string, Func<ValueToken, ValueToken>>();
            PostfixOperators = LanguageExt.Map.empty<string, Func<ValueToken, ValueToken>>();
            ConversionOperators = LanguageExt.Map.empty<string, Func<object, object>>();
            ToLstValues = x => x;
            ToMapValues = x => x;
            GenericType = null;
            FuncSpecs = funcSpecs;
            NodeName = nodeName;

            ValueParser = BuildObjectParser(funcSpecs).Memo();
        }

        /// <summary>
        /// Value type defintion constructor
        /// </summary>
        /// <param name="name"></param>
        /// <param name="mapsTo"></param>
        /// <param name="valueParser"></param>
        /// <param name="toLstValues"></param>
        /// <param name="toMapValues"></param>
        /// <param name="binaryOperators"></param>
        /// <param name="prefixOperators"></param>
        /// <param name="postfixOperators"></param>
        /// <param name="conversionOperators"></param>
        /// <param name="genericType"></param>
        public TypeDef(
            string name,
            Type mapsTo,
            Func<ProcessSystemConfigParser, Parser<object>> valueParser,
            Func<Lst<object>, object> toLstValues,
            Func<Map<string, object>, object> toMapValues,
            Map<string, Func<ValueToken, ValueToken, ValueToken>> binaryOperators,
            Map<string, Func<ValueToken, ValueToken>> prefixOperators,
            Map<string, Func<ValueToken, ValueToken>> postfixOperators,
            Map<string, Func<object, object>> conversionOperators,
            TypeDef genericType,
            int order
            )
        {
            if (valueParser == null) throw new ArgumentNullException(nameof(valueParser));

            MapsTo = mapsTo;
            Name = name;
            ValueParser = valueParser.Memo();
            BinaryOperators = binaryOperators ?? LanguageExt.Map.empty<string, Func<ValueToken, ValueToken, ValueToken>>();
            PrefixOperators = prefixOperators ?? LanguageExt.Map.empty<string, Func<ValueToken, ValueToken>>();
            PostfixOperators = postfixOperators ?? LanguageExt.Map.empty<string, Func<ValueToken, ValueToken>>();
            ConversionOperators = conversionOperators ?? LanguageExt.Map.empty<string, Func<object, object>>();
            ToLstValues = toLstValues;
            ToMapValues = toMapValues;
            GenericType = genericType;
        }

        private Func<ProcessSystemConfigParser, Parser<object>> BuildObjectParser(FuncSpec[] funcSpecs)
        {
            return p =>
                from state in getState<ParserState>()
                from funcs in many1(
                    p.token(
                        indented1(
                            from result in either(

                                    // Known function and property definitions
                                    choice(
                                        List.fold(funcSpecs, LanguageExt.Map.empty<string, Lst<FuncSpec>>(), (s, x) => s.AddOrUpdate(x.Name, Some: exists => exists.Add(x), None: () => List(x)))
                                        .Map((func, variants) =>

                                              // Hard-coded (for now) strategy match grammar
                                              func == "match" ? from m in p.match
                                                                select new NamedValueToken("match", m)

                                              // Hard-coded (for now) strategy redirect grammar
                                            : func == "redirect" ? from r in p.redirect
                                                                   select new NamedValueToken("redirect", r)

                                              // Attempt to parse the known properties and function definitions
                                            : from nam in attempt(p.reserved(func))
                                              from _ in p.symbol(":")
                                              from tok in choice(variants.Map(variant =>
                                                  from vals in p.arguments(nam, variant.Args)
                                                  let valmap = LanguageExt.Map.createRange(vals.Map(x => Tuple(x.Name, x)))
                                                  select new NamedValueToken(nam, new ValueToken(variant.Type(), variant.Body(valmap)))))
                                              select tok).Values.ToArray()),

                                    // Local value definitions
                                    from vd in p.valueDef
                                    from st in vd.Value.Type.Name == "cluster" && ((ClusterToken)vd.Value.Value).NodeName.Map(nn => nn.Equals(NodeName)).IfNone(false)
                                        ? from ins in getState<ParserState>().Map(s => s.SetCluster(((ClusterToken)vd.Value.Value)))
                                          from _ in setState(ins)
                                          select vd
                                        : result(vd)
                                    select vd
                                )
                            select result)))
                from newState in getState<ParserState>()
                from _ in setState(state.SetCluster(newState.Cluster))
                select Ctor == null
                    ? (object)funcs.Freeze()
                    : Ctor(funcs.Freeze());
        }


        static string MakeName(string name)
        {
            var res = parse(nameParser, name);
            if( res.IsFaulted || res.Reply.State.ToString().Length > 0)
            {
                if (res.IsFaulted)
                {
                    throw new ProcessConfigException(res.ToString());
                }
                else
                {
                    throw new ProcessConfigException($"Configuration parse error at {res.Reply.State.Pos}, near: {res.Reply.State.ToString()}");
                }
            }
            return res.Reply.Result;
        }

        static Parser<string> number =
            from x in digit
            from xs in many(digit)
            select new string(x.Cons(xs).ToArray());

        static Parser<string> initials =
            from x in upper
            from xs in many1(upper)
            select new string(x.Cons(xs).ToArray()).ToLower();

        static Parser<string> upperThenLower =
            from x in upper
            from xs in many1(lower)
            select new string(x.Cons(xs).ToArray()).ToLower();

        static Parser<string> lowerCase =
            asString(many1(lower));

        static Parser<string> underscores =
            from _ in many1(ch('_'))
            select "";

        static Parser<string> part =
            choice(
                number,
                attempt(initials),
                upperThenLower,
                lowerCase,
                underscores
                );

        static Parser<string> nameParser =
            from ps in many1(part)
            select String.Join("-", ps.Filter(x => !String.IsNullOrEmpty(x)));

        public Option<ValueToken> Convert(ValueToken token) =>
            token.Type == this
                ? token
                : ConversionOperators.ContainsKey(token.Type.Name)
                    ? Some(new ValueToken(this, ConversionOperators[token.Type.Name](token.Value)))
                    : None;

        public ValueToken BinaryOperator(string op, ValueToken lhs, ValueToken rhs) =>
            Convert(rhs)
                .Map(rhsconv =>
                    BinaryOperators.ContainsKey(op)
                        ? BinaryOperators[op](lhs, rhsconv)
                        : failwith<ValueToken>($"binary operator '{op}' not supported for {Name}"))
                .IfNone(() => failwith<ValueToken>($"binary operator '{op}' used with incompatible types {lhs.Type} and {rhs.Type}"));

        public ValueToken PrefixOperator(string op, ValueToken rhs) =>
            PrefixOperators.ContainsKey(op)
                ? PrefixOperators[op](rhs)
                : failwith<ValueToken>($"prefix operator '{op}' not supported for {Name}");

        public ValueToken PostfixOperator(string op, ValueToken lhs) =>
            PostfixOperators.ContainsKey(op)
                ? PostfixOperators[op](lhs)
                : failwith<ValueToken>($"postfix operator '{op}' not supported for {Name}");

        public override string ToString() =>
            GenericType == null
                ? Name
                : $"{Name}<{GenericType}>";

        static Map<TypeDef, TypeDef> maps = Map<TypeDef, TypeDef>();
        static Map<TypeDef, TypeDef> lists = Map<TypeDef, TypeDef>();

        public static TypeDef Map(Func<TypeDef> t)
        {
            if (maps.ContainsKey(t())) return maps[t()];

            var def = new TypeDef(
                "map",
                typeof(Map<string,object>),
                (ProcessSystemConfigParser p) =>
                    p.brackets(
                        from xs in p.commaSep(
                            from x in p.identifier
                            from _ in p.symbol(":")
                            from v in p.expr(t())
                            select Tuple(x, v.Value))
                        select (object)LanguageExt.Map.createRange(xs))
                    .label("map"),
                    list => failwith<Lst<object>>("Maps nested within lists not supported"),
                    map  => failwith<Map<string, object>>("Maps nested within maps not supported"),
                    LanguageExt.Map.create(
                        Types.OpT("+", () => maps[t()], (lhs, rhs) => (Map<string, object>)lhs + (Map<string, object>)lhs),
                        Types.OpT("-", () => maps[t()], (lhs, rhs) => (Map<string, object>)lhs - (Map<string, object>)lhs)
                    ), 
                null, 
                null, 
                null,
                t(),
                1);

            maps = maps.AddOrUpdate(t(), def);
            return def;
        }

        public static TypeDef Array(Func<TypeDef> t)
        {
            if (lists.ContainsKey(t())) return lists[t()];

            var def = new TypeDef(
                "array",
                typeof(Lst<object>),
                (ProcessSystemConfigParser p) =>
                    p.brackets(
                        from xs in p.commaSep(p.expr(t()))
                        select (object)xs)
                    .label("array"),
                    list => failwith<Lst<object>>("Maps nested within lists not supported"),
                    map  => failwith<Map<string, object>>("Maps nested within maps not supported"),
                    LanguageExt.Map.create(
                        Types.OpT("+", () => maps[t()], (lhs, rhs) => (Lst<object>)lhs + (Lst<object>)lhs),
                        Types.OpT("*", () => maps[t()], (lhs, rhs) => (Lst<object>)lhs * (Lst<object>)lhs),
                        Types.OpT("-", () => maps[t()], (lhs, rhs) => (Lst<object>)lhs - (Lst<object>)lhs)
                    ),
                null, 
                null, 
                null,
                t(),
                1);

            lists = lists.AddOrUpdate(t(), def);
            return def;
        }

        public static TypeDef Unknown = new TypeDef(
            "unknown",
            typeof(void),
            _ => failure<object>("unknown type"),
            x => x,
            x => x,
            null, null, null, null, null,
            Int32.MaxValue
        );

        public override int GetHashCode() =>
            Tuple(Name, GenericType).GetHashCode();

        public bool Equals(TypeDef other)
        {
            if (Object.ReferenceEquals(other, null)) return false;
            if (Name != other.Name) return false;
            if (Object.ReferenceEquals(GenericType, null) && Object.ReferenceEquals(other.GenericType, null)) return true;
            return GenericType == other.GenericType;
        }

        public int CompareTo(TypeDef other)
        {
            if (Object.ReferenceEquals(other, null)) return -1;
            var cmp = Name.CompareTo(other.Name);
            if (cmp != 0) return cmp;
            if (Object.ReferenceEquals(GenericType, null) && Object.ReferenceEquals(other.GenericType, null)) return 0;
            return GenericType.CompareTo(other);
        }
    }
}
