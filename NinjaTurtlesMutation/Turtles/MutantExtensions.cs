using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace NinjaTurtlesMutation.Turtles
{
    static class MutantExtensions
    {
        internal static SequencePoint GetCurrentSequencePoint(this MethodDefinition _method, int index)
        {
            var instruction = _method.Body.Instructions[index];
            var mapping = _method.DebugInformation.GetSequencePointMapping();

            SequencePoint seqPoint;

            while ((!mapping.TryGetValue(instruction, out seqPoint) || seqPoint.StartLine == 0xfeefee) && index > 0)
            {
                index--;
                instruction = _method.Body.Instructions[index];
            }

            mapping.TryGetValue(instruction, out seqPoint);
            return seqPoint;
        }

        

        internal static string GetOriginalSourceFileName(this MethodDefinition _method, int index)
        {
            var sequencePoint = _method.GetCurrentSequencePoint(index);/* ?? _method.Body.Instructions
                .Where(instruction => instruction.SequencePoint == null || instruction.SequencePoint.StartLine == 0xfeefee)
                .Select(instruction => instruction.SequencePoint)
                .FirstOrDefault();*/
            if (sequencePoint == null)
                return (null);
            return Path.GetFileName(sequencePoint.Document.Url);
        }
    }
}