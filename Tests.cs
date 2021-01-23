using AutoMapper;
using System;
using System.Collections.Generic;
using Xunit;
using System.Linq;
using FluentAssertions;
using AutoMapper.Extensions.EnumMapping;

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

    public class Tests
    {

        public static IEnumerable<object[]> Values = Enum.GetValues(typeof(Source)).Cast<Source>().Select(_ => new object [] { _ });

        private readonly MapperConfiguration mapperConfig;
        private readonly IMapper mapper;
        public Tests()
        {
            mapperConfig = new MapperConfiguration(config =>
            {
                config.CreateMap<SourceType, DestinationType>();

                config.CreateMap<Source, Destination>().ConvertUsingEnumMapping();

                config.Advanced.Validator(context => {

                    if (!context.Types.DestinationType.IsEnum) return;
                    if (!context.Types.SourceType.IsEnum) return;
                    if (context.TypeMap is not null) return;

                    throw new AutoMapperConfigurationException($"this.CreateMap<{context.Types.SourceType},{context.Types.DestinationType}>().ConvertUsingEnumMapping();");
                });

                config.EnableEnumMappingValidation();
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
            destination.Name.Should().Be(source.Name, "name should have been mapped");


            var isDestinationEnumDefined = Enum.IsDefined(typeof(Destination), destination.Enum);

            isDestinationEnumDefined.Should().BeTrue("what should we do with this value, when not?");
        }

        [Fact]
        public void MapperConfigurationIsValid() => mapperConfig.AssertConfigurationIsValid();
    }
}
