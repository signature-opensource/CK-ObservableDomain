using CK.Core;
using CK.Observable.Domain.Tests.Sample;
using FluentAssertions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.Observable.Domain.Tests
{
    [TestFixture]
    public class ExportTests
    {
        [Test]
        public void exporting_and_altering_simple()
        {
            var eventCollector = new TransactionEventCollector();

            var d = new ObservableDomain( eventCollector, TestHelper.Monitor );
            d.TransactionSerialNumber.Should().Be( 0, "Nothing happened yet." );

            string initial = d.ExportToString();

            d.Modify( () =>
            {
            } ).Should().NotBeNull( "A null list of events is because an error occurred." );

            d.TransactionSerialNumber.Should().Be( 1, "Even if nothing changed, TransactionNumber is incremented." );
            eventCollector.WriteEventsFrom( 0 ).Should().Be( "{}", "No event occured." );

            d.Modify( () =>
            {
                new Car( "Hello!" );
            } ).Should().NotBeNull( "A null list of events is because an error occurred." );

            string t2 = eventCollector.WriteEventsFrom( 0 );

            d.Modify( () =>
            {
                d.AllObjects.Single().Dispose();
            } ).Should().NotBeNull();

            string t3 = eventCollector.WriteEventsFrom( 2 );

            d.Modify( () =>
            {
                new MultiPropertyType();

            } ).Should().NotBeNull();

            string t4 = eventCollector.WriteEventsFrom( 3 );

            d.Modify( () =>
            {
                var m = d.AllObjects.OfType<MultiPropertyType>().Single();
                m.ChangeAll( "Pouf", 3, new Guid( "{B681AD83-A276-4A5C-A11A-4A22469B6A0D}" ) );

            } ).Should().NotBeNull();

            string t5 = eventCollector.WriteEventsFrom( 4 );

            d.Modify( () =>
            {
                var m = d.AllObjects.OfType<MultiPropertyType>().Single();
                m.SetDefaults();

            } ).Should().NotBeNull();

            string t6 = eventCollector.WriteEventsFrom( 5 );

        }

        [Test]
        public void exporting_and_altering_sample()
        {
            var eventCollector = new TransactionEventCollector();

            var d = SampleDomain.CreateSample( eventCollector );

            d.TransactionSerialNumber.Should().Be( 1 );

            string initial = d.ExportToString();

            TestHelper.Monitor.Info( initial );
            d.Modify( () =>
            {
                var g2 = d.AllObjects.OfType<Garage>().Single( g => g.CompanyName == null );
                g2.CompanyName = "Signature Code";
            } );
            string t1 = eventCollector.WriteEventsFrom( 1 );

            d.Modify( () =>
            {
                var g2 = d.AllObjects.OfType<Garage>().Single( g => g.CompanyName == "Signature Code" );
                g2.Cars.Clear();
                var newOne = new Mechanic( g2 ) { FirstName = "X", LastName = "Y" };
            } );
            string t2 = eventCollector.WriteEventsFrom( 2 );

        }
    }
}
