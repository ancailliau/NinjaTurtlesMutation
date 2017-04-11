﻿#region Copyright & licence

// This file is part of NinjaTurtles.
// 
// NinjaTurtles is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as
// published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version.
// 
// NinjaTurtles is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with NinjaTurtles.  If not, see <http://www.gnu.org/licenses/>.
// 
// Copyright (C) 2012-14 David Musgrove and othersR.

#endregion

using System.Linq;
using NinjaTurtlesMutation;
using NUnit.Framework;
using TestLibraryMono;
using TestLibraryNoPdb;

namespace NinjaTurtlesMutation.Tests
{
    [TestFixture]
    public class ModuleTests
    {
        [Test]
        public void Module_Loads_Definition()
        {
            var module = new Module(typeof(MutationTest).Assembly.Location);
            Assert.AreEqual("NinjaTurtlesMutation.dll", module.Definition.Name);
        }

        [Test]
        public void Module_Loads_Source_File_List()
        {
            var module = new Module(typeof(MutationTest).Assembly.Location);
            module.LoadDebugInformation();
            Assert.NotNull(module.SourceFiles.SingleOrDefault(s => s.Key.Contains("MutationTest.cs")));
        }

        [Test]
        public void Module_Loads_Debug_Information_For_Mono()
        {
            var module = new Module(typeof(TestClassMono).Assembly.Location);
            Mono.Cecil.MethodDefinition methodDefinition = module.Definition.Types
                .Single(t => t.Name == "TestClassMono")
                .Methods.Single(m => m.Name == "Run");
			var mapping = methodDefinition.DebugInformation.GetSequencePointMapping();
			Assert.IsTrue(methodDefinition
						  .Body.Instructions.All(i => !mapping.ContainsKey(i)));
            module.LoadDebugInformation();
//            Assert.IsTrue(module.Definition.Types
//                .Single(t => t.Name == "TestClassMono")
//                .Methods.Single(m => m.Name == "Run")
//                .Body.Instructions.Any(i => i.SequencePoint != null));
        }

        [Test]
        public void Module_Does_Not_Error_With_No_Debug_Information()
        {
			var module = new Module(typeof(TestClassNoPdb).Assembly.Location);
			Mono.Cecil.MethodDefinition methodDefinition = module.Definition.Types
				.Single(t => t.Name == "TestClassNoPdb")
				.Methods.Single(m => m.Name == "Run");
			var mapping = methodDefinition.DebugInformation.GetSequencePointMapping();
			Assert.IsTrue(methodDefinition
						  .Body.Instructions.All(i => !mapping.ContainsKey(i)));
            module.LoadDebugInformation();
            Assert.IsTrue(module.Definition.Types
                .Single(t => t.Name == "TestClassNoPdb")
                .Methods.Single(m => m.Name == "Run")
                .Body.Instructions.All(i => !mapping.ContainsKey(i)));
        }
    }
}
