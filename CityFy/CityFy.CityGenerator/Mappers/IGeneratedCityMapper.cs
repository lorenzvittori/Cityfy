using CityFy.CityGenerator.Models;
using ServiceDefault.City;

namespace CityFy.CityGenerator.Mappers;

public interface IGeneratedCityMapper
{
    GeneratedCityInfo ToInfo(GeneratedCity city);

    GeneratedCityDetail ToDetail(GeneratedCity city);
}
