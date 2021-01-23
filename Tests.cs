using AutoMapper;
using System;
using System.Collections.Generic;
using Xunit;
using System.Linq;
using FluentAssertions;

namespace Sample.AutoMapper.EnumValidation
{
    public enum Source
    {
        A,
        B,
        C
    }

    public enum Destination
    {
        A,
        B
    }


    class SourceType
    {
        public string Name { get; set; }
        public Source Enum { get; set; }
    }

    class DestinationType

    {
        public string Name { get; set; }
        public Destination Enum { get; set; }
    }



    public class SampleProfile : Profile
    {
        public SampleProfile()
        {
            CreateMap<SourceType, DestinationType>();
        }
    }


    public class Tests
    {

        public static IEnumerable<object[]> Values = Enum.GetValues(typeof(Source)).Cast<Source>().Select(_ => new object [] { _ });

        private readonly MapperConfiguration mapperConfig;
        private readonly IMapper mapper;
        public Tests()
        {
            mapperConfig = new MapperConfiguration(config =>
            {
            });

            mapper = mapperConfig.CreateMapper();
        }

        [Theory]
        [MemberData(nameof(Values))]
        public void CanMapSourceToDestination(Source value)
        {
            var source = new SourceType()
            {
                Name = Guid.NewGuid().ToString(),
                Enum = value
            };

            var destination = mapper.Map<DestinationType>(source);

            destination.Should().NotBeNull("mapping should have succeeded");
            destination.Name.Should().Be(source.Name, "Name should have been mapped");
        }

        [Fact]
        public void MapperConfigurationIsValid() => mapperConfig.AssertConfigurationIsValid();
    }
}
