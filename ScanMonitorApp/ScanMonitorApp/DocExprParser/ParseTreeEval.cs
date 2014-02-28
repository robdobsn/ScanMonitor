using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TinyPgDocExpr
{
    public partial class ParseTreeEval : ParseTree
    {
        public ParseTreeEval() : base()
        {
        }

        /// <summary>
        /// required to override this function from base otherwise the parsetree will consist of incorrect types of nodes
        /// </summary>
        /// <param name="token"></param>
        /// <param name="text"></param>
        /// <returns></returns>
        public override ParseNode CreateNode(Token token, string text)
        {
            ParseTreeEval node = new ParseTreeEval();
            node.Token = token;
            node.text = text;
            node.Parent = this;
            return node;
        }

        protected override object EvalStart(ParseTree tree, params object[] paramlist)
        {
            return this.GetValue(tree, TokenType.Expression, 0);
        }

        protected override object EvalLocatedString(ParseTree tree, params object[] paramlist)
        {
            TokenType type = this.nodes[0].Token.Type;
            Console.WriteLine("NodeCount = " + tree.Nodes.Count);
            Console.WriteLine("NodesCount = " + nodes.Count);
            object stringLit = this.GetValue(tree, TokenType.STRINGLITERAL, 0);
            Console.WriteLine((string)stringLit);
            return false;
        }

        protected override object EvalParenthesizedExpression(ParseTree tree, params object[] paramlist)
        {
            Console.WriteLine("NodeCount = " + tree.Nodes.Count);
            Console.WriteLine("NodesCount = " + nodes.Count);
            return this.GetValue(tree, TokenType.Expression, 0);
        }

        protected override object EvalPrimaryExpression(ParseTree tree, params object[] paramlist)
        {
            Console.WriteLine("NodeCount = " + tree.Nodes.Count);
            Console.WriteLine("NodesCount = " + nodes.Count);
            object retVal = this.GetValue(tree, TokenType.LocatedString, 0);
            if (retVal == null)
                retVal = this.GetValue(tree, TokenType.ParenthesizedExpression, 0);
            return retVal;
        }

        protected override object EvalUnaryExpression(ParseTree tree, params object[] paramlist)
        {
            Console.WriteLine("NodeCount = " + tree.Nodes.Count);
            Console.WriteLine("NodesCount = " + nodes.Count);
            return this.GetValue(tree, TokenType.PrimaryExpression, 0);
        }

        protected override object EvalExpression(ParseTree tree, params object[] paramlist)
        {
            object result = this.GetValue(tree, TokenType.UnaryExpression, 0);
            Console.WriteLine("NodeCount = " + tree.Nodes.Count);
            Console.WriteLine("NodesCount = " + nodes.Count);
            for (int i = 1; i < nodes.Count; i += 2)
            {
                Token token = nodes[i].Token;
                object val = nodes[i+1].Eval(tree, paramlist);
                if (token.Type == TokenType.PIPE)
                    result = Convert.ToBoolean(result) || Convert.ToBoolean(val);
                else if (token.Type == TokenType.AMP)
                    result = Convert.ToBoolean(result) && Convert.ToBoolean(val);
            }
            return result;
        }
    }
}
