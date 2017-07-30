/* (c) 2017 James Renwick */
using System;
using System.Collections.Generic;
using System.Linq;

namespace cci
{
    public class Test
    {
        /// <summary>
        /// Gets the name of the test.
        /// </summary>
        public string Name { get; private set; }
        /// <summary>
        /// Gets whether the test was executed.
        /// </summary>
        public bool Ran { get; private set; }
        /// <summary>
        /// Gets whether the test succeeded.
        /// </summary>
        public bool Succeeded { get; private set; }
        /// <summary>
        /// Gets the tests output in lines of text.
        /// </summary>
        public string[] Output { get; private set; }
        /// <summary>
        /// Gets a summary of the test's execution.
        /// </summary>
        public string Summary { get; private set; }

        /// <summary>
        /// Creates a new Test object.
        /// </summary>
        /// <param name="name">The name of the test</param>
        /// <param name="ran">Whether the test ran</param>
        /// <param name="succeeded">Whether the test succeeded</param>
        /// <param name="output">The output of the test</param>
        /// <param name="summary">The summary of the test's execution</param>
        public Test(string name, bool ran, bool succeeded, string[] output, string summary)
        {
            Name = name;
            Ran = ran;
            Succeeded = succeeded;
            Output = output;
            Summary = summary;
        }
    }


    public class TestSuite
    {
        /// <summary>
        /// Gets the name of the test suite.
        /// </summary>
        public string Name { get; private set; }
        /// <summary>
        /// Gets whether the test suite was executed.
        /// </summary>
        public bool Ran { get; private set; }
        /// <summary>
        /// Gets whether all necessary tests in the suite succeeded.
        /// </summary>
        public bool Succeeded { get; private set; }
    }

    public class TestResults
    {
        /// <summary>
        /// Gets the total number of tests run.
        /// </summary>
        public int TestsRun { get; private set; }
        /// <summary>
        /// Gets the total number of tests which succeeded.
        /// </summary>
        public int TestsSucceeded { get; private set; }
        /// <summary>
        /// Gets the total number of tests which failed.
        /// </summary>
        public int TestsFailed { get; private set; }
        /// <summary>
        /// Gets the results of all available tests.
        /// </summary>
        public Test[] Tests { get; private set; }
        /// <summary>
        /// Gets the results of all available test suites.
        /// </summary>
        public TestSuite[] TestSuites { get; private set; }


        /// <summary>
        /// Creates a new TestResults object.
        /// </summary>
        /// <param name="tests">The results of the available tests.</param>
        /// <param name="suites">The results of the available test suites.</param>
        public TestResults(Test[] tests, TestSuite[] suites)
        {
            Tests = tests;
            TestSuites = suites;

            foreach (var test in tests)
            {
                if (test.Ran) TestsRun++;
                if (test.Succeeded) TestsSucceeded++;
                if (test.Ran && !test.Succeeded) TestsFailed++;
            }
        }
    }


    public interface ITestResultParser
    {
        TestResults ParseOutput(CommandResult output);
    }


    public class OSTestParser : ITestResultParser
    {
        public TestResults ParseOutput(CommandResult output)
        {
            var tests = new List<Test>();
            var suites = new List<TestSuite>();

            foreach (var line in output.Lines)
            {
                if (line.Text.StartsWith("[PASS]")) {
                    tests.Add(new Test("Test", true, true, null, line.Text));
                }
                else if (line.Text.StartsWith("[FAIL]")) {
                    tests.Add(new Test("Test", true, false, null, line.Text));
                }
            }
            return new TestResults(tests.ToArray(), new TestSuite[0]);
        }
    }
}
