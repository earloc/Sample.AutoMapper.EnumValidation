using AutoMapper;
using System;
using System.Collections.Generic;
using Xunit;
using System.Linq;
using FluentAssertions;
using AutoMapper.Extensions.EnumMapping;
using Xunit.Abstractions;

namespace Sample.AutoMapper.EnumValidation
{
    public enum Source { A, B, C, D, Executer, A1, B2, C3 }

    public enum Destination { c, b, X, Y, A, Executor }


    class SourceType
    {
        public Source[] Enums { get; set; }
    }

    class DestinationType
    {
        public Destination[] Enums { get; set; }
    }

    public class Tests
    {
        public static IEnumerable<object[]> Values = Enum.GetValues(typeof(Source)).Cast<Source>().Select(_ => new object [] { _ });

        private readonly MapperConfiguration mapperConfig;
        private readonly IMapper mapper;
        private readonly ITestOutputHelper testOutput;

        public Tests(ITestOutputHelper testOutput)
        {
            mapperConfig = new MapperConfiguration(config =>
            {
                config.CreateMap<SourceType, DestinationType>();

                config.CreateMap<Source, Destination>().ConvertUsingEnumMapping(opt => opt.MapByName());

                config.Advanced.Validator(context =>
                {
                    if (!context.Types.DestinationType.IsEnum) return;
                    if (!context.Types.SourceType.IsEnum) return;
                    if (context.TypeMap is not null) return;

                    var message = $"config.CreateMap<{context.Types.SourceType}, {context.Types.DestinationType}>().ConvertUsingEnumMapping(opt => opt.MapByName());";

                    throw new AutoMapperConfigurationException(message);
                });

                config.EnableEnumMappingValidation();
            });

            mapper = mapperConfig.CreateMapper();
            this.testOutput = testOutput;
        }

        [Fact]
        public void ShowcaseTheProblems()
        {
            var problematicMapper = new MapperConfiguration(config =>
            {
                config.CreateMap<SourceType, DestinationType>();
            }).CreateMapper();

            var destination = problematicMapper.Map<DestinationType>(new SourceType()
            {
                Enums = new []
                {
                    Source.A,
                    Source.B,
                    Source.C,
                    Source.D,
                    Source.Executer,
                    Source.A1,
                    Source.B2,
                    Source.C3
                }
            });

            var mappedValues = destination.Enums.Select(x => x.ToString()).ToArray();
            
            testOutput.WriteLine(string.Join(Environment.NewLine, mappedValues));
            /*  
                Source.A            => A           <- ok
                Source.B            => B           <- ok
                Source.C            => C           <- ok
                Source.D            => Y           <- whoops
                Source.Executer     => A           <- wait, what?
                Source.A1           => Executor    <- nah
                Source.B2           => 6           <- wtf?
                Source.C3           => 7           <- wth?
             */
        }


        [Fact]
        public void MapperConfigurationIsValid() => mapperConfig.AssertConfigurationIsValid();
    }
}