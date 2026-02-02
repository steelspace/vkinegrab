using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using vkinegrab.Models;
using vkinegrab.Services;
using Xunit;

namespace vkinegrab.Tests
{
    public class SchedulesStoreServiceTests
    {
        [Fact]
        public async Task StoresSchedulesAndVenues_DeduplicatesVenues()
        {
            // Arrange
            var schedules = new List<Schedule>
            {
                new Schedule { Date = new System.DateOnly(2026,2,1), MovieId = 1, MovieTitle = "A" },
                new Schedule { Date = new System.DateOnly(2026,2,1), MovieId = 2, MovieTitle = "B" }
            };

            var venues = new List<Venue>
            {
                new Venue { Id = 1, Name = "C1" },
                new Venue { Id = 1, Name = "C1 Duplicate" }, // duplicate id should be deduped
                new Venue { Id = 2, Name = "C2" },
                new Venue { Id = 0, Name = "Invalid" } // should be ignored
            };

            var perfMock = new Mock<vkinegrab.Services.IPerformancesService>();
            perfMock.Setup(p => p.GetSchedulesWithVenues(It.IsAny<System.Uri>(), It.IsAny<string>(), default))
                    .ReturnsAsync(((IReadOnlyList<Schedule>)schedules, (IReadOnlyList<Venue>)venues));

            var dbMock = new Mock<vkinegrab.Services.IDatabaseService>();
            dbMock.Setup(d => d.StoreSchedule(It.IsAny<Schedule>())).Returns(Task.CompletedTask);
            dbMock.Setup(d => d.StoreVenues(It.IsAny<IEnumerable<Venue>>())).Returns(Task.CompletedTask);

            var service = new SchedulesStoreService(perfMock.Object, dbMock.Object);

            // Act
            var (fetchedSchedules, storedSchedules, failedSchedules, storedVenues, failedVenues) = await service.StoreSchedulesAndVenuesAsync(null, "all");

            // Assert
            Assert.Collection(fetchedSchedules, s => Assert.NotNull(s), s => Assert.NotNull(s));
            Assert.Equal(2, storedSchedules);
            Assert.Equal(0, failedSchedules);
            // venues deduped by Id and Id==0 ignored => ids {1,2}
            Assert.Equal(2, storedVenues);
            Assert.Equal(0, failedVenues);

            dbMock.Verify(d => d.StoreSchedule(It.Is<Schedule>(s => s.MovieId == 1)), Times.Once);
            dbMock.Verify(d => d.StoreSchedule(It.Is<Schedule>(s => s.MovieId == 2)), Times.Once);

            dbMock.Verify(d => d.StoreVenues(It.Is<IEnumerable<Venue>>(v => v.Select(x => x.Id).OrderBy(i => i).SequenceEqual(new[] { 1, 2 }))), Times.Once);
        }

        [Fact]
        public async Task StoresProvidedSchedulesAndVenues_DeduplicatesVenues()
        {
            var schedules = new List<Schedule>
            {
                new Schedule { Date = new System.DateOnly(2026,2,1), MovieId = 10, MovieTitle = "X" }
            };

            var venues = new List<Venue>
            {
                new Venue { Id = 7, Name = "V7" },
                new Venue { Id = 7, Name = "V7 dup" },
                new Venue { Id = 8, Name = "V8" }
            };

            var perfMock = new Mock<vkinegrab.Services.IPerformancesService>();
            var dbMock = new Mock<vkinegrab.Services.IDatabaseService>();
            dbMock.Setup(d => d.StoreSchedule(It.IsAny<Schedule>())).Returns(Task.CompletedTask);
            dbMock.Setup(d => d.StoreVenues(It.IsAny<IEnumerable<Venue>>())).Returns(Task.CompletedTask);

            var service = new SchedulesStoreService(perfMock.Object, dbMock.Object);

            var (storedSchedules, failedSchedules, storedVenues, failedVenues) = await service.StoreSchedulesAndVenuesAsync(schedules, venues);

            Assert.Equal(1, storedSchedules);
            Assert.Equal(0, failedSchedules);
            Assert.Equal(2, storedVenues);
            Assert.Equal(0, failedVenues);

            dbMock.Verify(d => d.StoreSchedule(It.IsAny<Schedule>()), Times.Once);
            dbMock.Verify(d => d.StoreVenues(It.Is<IEnumerable<Venue>>(v => v.Select(x => x.Id).OrderBy(i => i).SequenceEqual(new[] { 7, 8 }))), Times.Once);
        }

        [Fact]
        public async Task FetchAndStore_ReturnsFetchedSchedules_AndCounts()
        {
            var schedules = new List<Schedule>
            {
                new Schedule { Date = new System.DateOnly(2026,2,5), MovieId = 33, MovieTitle = "Y" }
            };

            var venues = new List<Venue>
            {
                new Venue { Id = 3, Name = "VV" }
            };

            var perfMock = new Mock<vkinegrab.Services.IPerformancesService>();
            perfMock.Setup(p => p.GetSchedulesWithVenues(It.IsAny<System.Uri>(), It.IsAny<string>(), default))
                    .ReturnsAsync(((IReadOnlyList<Schedule>)schedules, (IReadOnlyList<Venue>)venues));

            var dbMock = new Mock<vkinegrab.Services.IDatabaseService>();
            dbMock.Setup(d => d.StoreSchedule(It.IsAny<Schedule>())).Returns(Task.CompletedTask);
            dbMock.Setup(d => d.StoreVenues(It.IsAny<IEnumerable<Venue>>())).Returns(Task.CompletedTask);

            var service = new SchedulesStoreService(perfMock.Object, dbMock.Object);

            var (fetchedSchedules, storedSchedules, failedSchedules, storedVenues, failedVenues) = await service.StoreSchedulesAndVenuesAsync(null, "all");

            Assert.Single(fetchedSchedules);
            Assert.Equal(1, storedSchedules);
            Assert.Equal(0, failedSchedules);
            Assert.Equal(1, storedVenues);
            Assert.Equal(0, failedVenues);
        }

        [Fact]
        public async Task ReturnsFailedCounts_WhenStoreThrows()
        {
            // Arrange
            var schedules = new List<Schedule>
            {
                new Schedule { Date = new System.DateOnly(2026,2,1), MovieId = 1, MovieTitle = "A" },
            };

            var venues = new List<Venue>
            {
                new Venue { Id = 5, Name = "C5" }
            };

            var perfMock = new Mock<vkinegrab.Services.IPerformancesService>();
            perfMock.Setup(p => p.GetSchedulesWithVenues(It.IsAny<System.Uri>(), It.IsAny<string>(), default))
                    .ReturnsAsync(((IReadOnlyList<Schedule>)schedules, (IReadOnlyList<Venue>)venues));

            var dbMock = new Mock<vkinegrab.Services.IDatabaseService>();
            dbMock.Setup(d => d.StoreSchedule(It.IsAny<Schedule>())).ThrowsAsync(new System.InvalidOperationException("boom"));
            dbMock.Setup(d => d.StoreVenues(It.IsAny<IEnumerable<Venue>>())).ThrowsAsync(new System.InvalidOperationException("boom"));

            var service = new SchedulesStoreService(perfMock.Object, dbMock.Object);

            // Act
            var (fetchedSchedules, storedSchedules, failedSchedules, storedVenues, failedVenues) = await service.StoreSchedulesAndVenuesAsync(null, "all");

            // Assert
            Assert.Single(fetchedSchedules);
            Assert.Equal(0, storedSchedules);
            Assert.Equal(1, failedSchedules);
            Assert.Equal(0, storedVenues);
            Assert.Equal(1, failedVenues);
        }
    }
}
