﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nncase.Pattern;
using static Nncase.Pattern.F.Math;
using static Nncase.Pattern.F.Tensors;
using static Nncase.Pattern.F.NN;
using static Nncase.Pattern.Utility;
using Nncase.IR;
using Nncase.Evaluator;
using Nncase.IR.Tensors;

namespace Nncase.Transform.DataFlow.Rules
{
    public class FoldConstCall : PatternRule
    {
        public FoldConstCall()
        {
            Pattern = IsCall(IsWildCard(), IsVArgsRepeat(() => IsAlt(IsConst(), IsConstTuple())));
        }
        
        public override Expr? GetRePlace(IMatchResult result)
        {
            var expr = result[Pattern];
            var tensor = expr.Eval();
            var callSlice = (expr is Call call) && (call.Target == new Slice());
            return callSlice ? tensor.ToConst(expr.CheckedShape) : tensor.ToConst();
        }
    }

    public class FoldConstFunction : PatternRule
    {
        public FoldConstFunction()
        {
            Pattern = IsFunction(IsWildCard(), IsVArgsRepeat(() => IsAlt(IsConst(), IsConstTuple())));
        }
        public override Expr? GetRePlace(IMatchResult result) => Evaluator.Evaluator.Eval(result[Pattern]).ToConst();
    }

    public class FoldShapeOp : PatternRule
    {
        WildCardPattern wc = "input";

        public FoldShapeOp()
        {
            Pattern = ShapeOp(wc);
        }

        public override Expr? GetRePlace(IMatchResult result)
        {
            return Const.FromShape(result[wc].CheckedShape);
        }
    }
}
